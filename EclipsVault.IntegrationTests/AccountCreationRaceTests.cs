using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Two administrators create accounts at once on each database. Both pass the checks before either
/// saves, so the database's unique indexes decide: a lost username or address is the check's conflict.
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
        var request = new CreateUserRequest(username, "Race", "Person", "race.person@example.test", ClearanceLevel.Standard, "GLOBAL");

        var failures = await Task.WhenAll(
            Task.Run(() => Record.ExceptionAsync(() => UserAdmin(first, bothChecked).CreateAsync(request, default))),
            Task.Run(() => Record.ExceptionAsync(() => UserAdmin(second, bothChecked).CreateAsync(request, default))));

        var conflict = Assert.IsType<UsernameTakenException>(Assert.Single(failures, failure => failure is not null));
        Assert.Equal($"The username '{username}' is already taken.", conflict.Message);
        await using var check = databases.NewRequest();
        var account = await check.Context.Users.AsNoTracking().SingleAsync(user => user.Username == username);
        var created = await check.Context.AuditLogs.AsNoTracking()
            .Where(row => row.Action == AuditAction.UserCreated && row.Details!.Contains(username))
            .ToListAsync();
        Assert.Equal(account.Id, Assert.Single(created).ResourceId);
    }

    [Fact]
    public async Task Two_creates_for_one_mailbox_with_different_usernames_make_one_account_and_answer_the_other_with_the_conflict()
    {
        const string mailbox = "race.mailbox@example.test";
        using var bothChecked = new Barrier(participantCount: 2);
        await using var first = databases.NewRequest();
        await using var second = databases.NewRequest();

        var failures = await Task.WhenAll(
            Task.Run(() => Record.ExceptionAsync(() => UserAdmin(first, bothChecked).CreateAsync(
                new CreateUserRequest("it-race-address-one", "Address", "Race", mailbox, ClearanceLevel.Standard, "GLOBAL"), default))),
            Task.Run(() => Record.ExceptionAsync(() => UserAdmin(second, bothChecked).CreateAsync(
                new CreateUserRequest("it-race-address-two", "Address", "Race", mailbox, ClearanceLevel.Standard, "GLOBAL"), default))));

        Assert.IsType<EmailAddressTakenException>(Assert.Single(failures, failure => failure is not null));
        await using var check = databases.NewRequest();
        Assert.Single(await check.Context.Users.AsNoTracking().Where(user => user.Email == mailbox).ToListAsync());
        Assert.Single(await check.Context.AccountInvitations.AsNoTracking()
            .Where(invitation => invitation.User!.Email == mailbox).ToListAsync());
    }

    private static UserAdminService UserAdmin(AccountRequest request, Barrier bothChecked) => new(
        request.Users,
        new AccountInvitationRepository(request.Context),
        new UndeliveredInvitations(),
        new UnusedSessionStore(),
        request.RecoveryCodes,
        new UnusedAuditSink(),
        new CommitterThatWaitsForTheOtherCreate(request.Changes, bothChecked),
        new Administrator(),
        TimeProvider.System);

    /// <summary>Invitations are configured; whether one is delivered after the create commits is not in question here.</summary>
    private sealed class UndeliveredInvitations : IAccountInvitationDelivery
    {
        public bool Available => true;
        public Task<bool> TrySendAsync(string address, string displayName, string username, string token, CancellationToken ct)
            => Task.FromResult(false);
    }

    /// <summary>
    /// A create checks the username and address before it commits, so holding each create's commit here
    /// until the other arrives makes both check before either saves.
    /// </summary>
    private sealed class CommitterThatWaitsForTheOtherCreate(IAccountChangeCommitter inner, Barrier bothChecked) : IAccountChangeCommitter
    {
        public Task<bool> CommitAsync(User account, AuditEntry entry, CancellationToken ct)
        {
            Assert.True(bothChecked.SignalAndWait(TimeSpan.FromSeconds(10)), "The other create never reached its commit.");
            return inner.CommitAsync(account, entry, ct);
        }

        public Task<bool> CommitAsync(User account, AuditEntry entry, Func<CancellationToken, Task> recheck, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CommitAdministratorChangeAsync(User account, AuditEntry entry, Func<CancellationToken, Task> recheck, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CommitAsync(User account, AuditAction action, string details, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CommitAsync(User account, AuditAction action, string details, Func<CancellationToken, Task> recheck, CancellationToken ct) => throw new NotSupportedException();
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
