using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Notifications;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Notifications;
using EclipsVault.Infrastructure.Persistence.Repositories;
using EclipsVault.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Two administrators create the same username at once on each database. Both pass the username
/// check before either saves, so the database's unique index decides, and the loser hears the same
/// conflict the check gives.
/// </summary>
public abstract class AccountCreationRaceTests(AccountChangeDatabases databases)
{
    [Fact]
    public async Task Two_creates_of_one_username_make_one_account_and_answer_the_other_with_the_conflict()
    {
        const string username = "it-race-same-username";
        using var bothChecked = new Barrier(participantCount: 2);
        await using var first = databases.NewRequest();
        await using var second = databases.NewRequest();
        var request = new CreateUserRequest(username, "Race", "Person", "It-race-initial-password", ClearanceLevel.Standard, "GLOBAL");

        var failures = await Task.WhenAll(
            Task.Run(() => Record.ExceptionAsync(() => UserAdmin(first, bothChecked).CreateAsync(request, default))),
            Task.Run(() => Record.ExceptionAsync(() => UserAdmin(second, bothChecked).CreateAsync(request, default))));

        var conflict = Assert.IsType<VaultAdminException>(Assert.Single(failures, failure => failure is not null));
        Assert.Equal($"The username '{username}' is already taken.", conflict.Message);
        await using var check = databases.NewRequest();
        var account = await check.Context.Users.AsNoTracking().SingleAsync(user => user.Username == username);
        var created = await check.Context.AuditLogs.AsNoTracking()
            .Where(row => row.Action == AuditAction.UserCreated && row.Details!.Contains(username))
            .ToListAsync();
        Assert.Equal(account.Id, Assert.Single(created).ResourceId);
    }

    private static UserAdminService UserAdmin(AccountRequest request, Barrier bothChecked) => new(
        request.Users,
        new HasherThatWaitsForTheOtherCreate(bothChecked),
        new UnusedSessionStore(),
        request.RecoveryCodes,
        new CleanBreachScreen(),
        UserDirectoryOptions.Default,
        new NotificationService(new LoggingEmailSender(NullLogger<LoggingEmailSender>.Instance),
            new EmailLogRepository(request.Context), request.Users, new NotificationOptions(false), TimeProvider.System),
        new UnusedAuditSink(),
        request.Changes,
        new Administrator(),
        TimeProvider.System);

    /// <summary>
    /// A create hashes the password after it checks the username and before it saves, so holding each
    /// create here until the other arrives makes both check before either saves.
    /// </summary>
    private sealed class HasherThatWaitsForTheOtherCreate(Barrier bothChecked) : IPasswordHasher
    {
        public PasswordHashResult Hash(string password)
        {
            Assert.True(bothChecked.SignalAndWait(TimeSpan.FromSeconds(10)), "The other create never reached its save.");
            return new PasswordHashResult(new byte[32], new byte[16]);
        }

        public bool Verify(string password, byte[] hash, byte[] salt) => throw new NotSupportedException();
    }

    private sealed class CleanBreachScreen : IBreachedPasswordScreen
    {
        public bool IsCompromised(string password) => false;
        public int CorpusSize => 0;
    }

    private sealed class UnusedSessionStore : ISessionRevocationService
    {
        public Task<string> CaptureAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> FindAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RevokeAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>An account's creation is recorded in its own commit, never written to the sink on its own.</summary>
    private sealed class UnusedAuditSink : IAuditSink
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Administrator : IAuditContext
    {
        public Guid? UserId => null;
        public string? Username => "vault-admin";
        public string? SourceIp => "192.0.2.1";
    }
}

[Collection(SqlServerAccountChangeCollection.Name)]
public sealed class SqlServerAccountCreationRaceTests(SqlServerAccountChangeDatabases databases) : AccountCreationRaceTests(databases);

[Collection(PostgresAccountChangeCollection.Name)]
public sealed class PostgresAccountCreationRaceTests(PostgresAccountChangeDatabases databases) : AccountCreationRaceTests(databases);
