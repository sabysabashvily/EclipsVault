using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Notifications;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Notifications;
using EclipsVault.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The vault's only two enabled administrators each remove the other at the same moment, on each
/// database. Both requests reach their commit before either saves, so only the vault-wide
/// administrators lock decides: one change commits, and the other is refused because it would leave
/// no enabled administrator. Each test has a database of its own, so no other test's administrators count.
/// </summary>
public abstract class AdministratorRaceTests(AccountChangeDatabases databases)
{
    [Fact]
    public async Task Two_administrators_demoting_each_other_at_once_leave_one_enabled_administrator()
    {
        const string database = "administrators_demote_each_other";
        await MigrateAsync(database);
        var alice = await AddAdministratorAsync(database, "it-race-alice");
        var bob = await AddAdministratorAsync(database, "it-race-bob");
        using var bothReady = new Barrier(participantCount: 2);
        await using var aliceRequest = databases.NewRequest(database: database);
        await using var bobRequest = databases.NewRequest(database: database);

        var failures = await Task.WhenAll(
            Task.Run(() => Record.ExceptionAsync(() => UserAdmin(aliceRequest, alice.Id, bothReady).SetRoleAsync(bob.Id, ClearanceLevel.Secret, "GLOBAL", default))),
            Task.Run(() => Record.ExceptionAsync(() => UserAdmin(bobRequest, bob.Id, bothReady).SetRoleAsync(alice.Id, ClearanceLevel.Secret, "GLOBAL", default))));

        var refusal = Assert.IsType<LastAdministratorException>(Assert.Single(failures, failure => failure is not null));
        Assert.Equal("Cannot demote the last TopSecret administrator — the vault would become unmanageable.", refusal.Message);
        Assert.Single(await EnabledAdministratorsAsync(database));
        Assert.Equal([AuditAction.UserRoleChanged], await AdministratorEventsAsync(database));
    }

    [Fact]
    public async Task A_demotion_and_a_disable_at_once_leave_one_enabled_administrator()
    {
        const string database = "administrators_demote_against_disable";
        await MigrateAsync(database);
        var alice = await AddAdministratorAsync(database, "it-race-alice");
        var bob = await AddAdministratorAsync(database, "it-race-bob");
        using var bothReady = new Barrier(participantCount: 2);
        await using var aliceRequest = databases.NewRequest(database: database);
        await using var bobRequest = databases.NewRequest(database: database);

        var failures = await Task.WhenAll(
            Task.Run(() => Record.ExceptionAsync(() => UserAdmin(aliceRequest, alice.Id, bothReady).SetEnabledAsync(bob.Id, enabled: false, default))),
            Task.Run(() => Record.ExceptionAsync(() => UserAdmin(bobRequest, bob.Id, bothReady).SetRoleAsync(alice.Id, ClearanceLevel.Secret, "GLOBAL", default))));

        Assert.IsType<LastAdministratorException>(Assert.Single(failures, failure => failure is not null));
        Assert.Single(await EnabledAdministratorsAsync(database));
        Assert.Single(await AdministratorEventsAsync(database));
    }

    private async Task MigrateAsync(string database)
    {
        await using var setup = databases.NewRequest(database: database);
        await setup.Context.GetService<IMigrator>().MigrateAsync();
    }

    private async Task<User> AddAdministratorAsync(string database, string username)
    {
        var administrator = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            DisplayName = username,
            Email = $"{username}@example.test",
            PasswordHash = new byte[32],
            PasswordSalt = new byte[16],
            Clearance = ClearanceLevel.TopSecret,
            ProjectKey = "GLOBAL",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await using var request = databases.NewRequest(database: database);
        await request.Users.AddAsync(administrator, default);
        return administrator;
    }

    private async Task<List<string>> EnabledAdministratorsAsync(string database)
    {
        await using var check = databases.NewRequest(database: database);
        return await check.Context.Users.AsNoTracking()
            .Where(user => user.Clearance == ClearanceLevel.TopSecret && !user.IsDisabled)
            .Select(user => user.Username)
            .ToListAsync();
    }

    private async Task<List<AuditAction>> AdministratorEventsAsync(string database)
    {
        await using var check = databases.NewRequest(database: database);
        return await check.Context.AuditLogs.AsNoTracking()
            .Where(row => row.Action == AuditAction.UserRoleChanged || row.Action == AuditAction.UserDisabled)
            .Select(row => row.Action)
            .ToListAsync();
    }

    private static UserAdminService UserAdmin(AccountRequest request, Guid signedInAs, Barrier bothReady) => new(
        request.Users,
        new UnusedHasher(),
        new UnusedSessionStore(),
        request.RecoveryCodes,
        new UnusedBreachScreen(),
        UserDirectoryOptions.Default,
        new NotificationService(new LoggingEmailSender(NullLogger<LoggingEmailSender>.Instance),
            new EmailLogRepository(request.Context), request.Users, new NotificationOptions(false), TimeProvider.System),
        new UnusedAuditSink(),
        new CommitterThatWaitsForTheOtherChange(request.Changes, bothReady),
        new SignedIn(signedInAs),
        TimeProvider.System);

    /// <summary>
    /// Holds each change at its commit until the other arrives, so both have read their account and
    /// passed every check made before the commit when either starts saving.
    /// </summary>
    private sealed class CommitterThatWaitsForTheOtherChange(IAccountChangeCommitter inner, Barrier bothReady) : IAccountChangeCommitter
    {
        public Task<bool> CommitAsync(User account, AuditEntry entry, CancellationToken ct)
        {
            WaitForTheOtherChange();
            return inner.CommitAsync(account, entry, ct);
        }

        public Task<bool> CommitAdministratorChangeAsync(User account, AuditEntry entry, Func<CancellationToken, Task> recheck, CancellationToken ct)
        {
            WaitForTheOtherChange();
            return inner.CommitAdministratorChangeAsync(account, entry, recheck, ct);
        }

        public Task<bool> CommitAsync(User account, AuditAction action, string details, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CommitAsync(User account, AuditAction action, string details, Func<CancellationToken, Task> recheck, CancellationToken ct) => throw new NotSupportedException();

        private void WaitForTheOtherChange()
            => Assert.True(bothReady.SignalAndWait(TimeSpan.FromSeconds(10)), "The other change never reached its commit.");
    }

    private sealed class SignedIn(Guid accountId) : IAuditContext
    {
        public Guid? UserId => accountId;
        public string? Username => "it-race-administrator";
        public string? SourceIp => "192.0.2.1";
    }

    private sealed class UnusedHasher : IPasswordHasher
    {
        public PasswordHashResult Hash(string password) => throw new NotSupportedException();
        public bool Verify(string password, byte[] hash, byte[] salt) => throw new NotSupportedException();
    }

    private sealed class UnusedBreachScreen : IBreachedPasswordScreen
    {
        public bool IsCompromised(string password) => throw new NotSupportedException();
        public int CorpusSize => 0;
    }

    private sealed class UnusedSessionStore : ISessionRevocationService
    {
        public Task<string> CaptureAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> FindAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RevokeAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>A role change or disable is recorded in its own commit, never written to the sink on its own.</summary>
    private sealed class UnusedAuditSink : IAuditSink
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken ct) => throw new NotSupportedException();
    }
}

[Collection(SqlServerAccountChangeCollection.Name)]
public sealed class SqlServerAdministratorRaceTests(SqlServerAccountChangeDatabases databases) : AdministratorRaceTests(databases);

[Collection(PostgresAccountChangeCollection.Name)]
public sealed class PostgresAdministratorRaceTests(PostgresAccountChangeDatabases databases) : AdministratorRaceTests(databases);
