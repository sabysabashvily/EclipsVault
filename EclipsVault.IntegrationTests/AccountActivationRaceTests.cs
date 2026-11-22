using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence.Repositories;
using EclipsVault.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// An invitation is used by two requests at once, on each database. One request runs after the other
/// has loaded the account and before it commits, so the account's stamp decides: exactly one change takes effect.
/// </summary>
public abstract class AccountActivationRaceTests(AccountChangeDatabases databases)
{
    private const string Password = "It-activation-race-password";
    private static readonly TotpService Authenticator = new(TimeProvider.System);

    [Fact]
    public async Task Two_activations_of_one_invitation_activate_the_account_once()
    {
        var (account, token, seed) = await SeedInvitedAccountAsync("it-race-activate-twice");
        var code = await FreshCodeAsync(seed);
        using var bothChecked = new Barrier(participantCount: 2);
        await using var first = databases.NewRequest();
        await using var second = databases.NewRequest();

        var outcomes = await Task.WhenAll(
            Task.Run(() => Activation(first, bothChecked).CompleteAsync(token, Password, code, default)),
            Task.Run(() => Activation(second, bothChecked).CompleteAsync(token, Password, code, default)));

        Assert.Equal([ActivationOutcome.Activated, ActivationOutcome.InvitationInvalid], outcomes.Order());
        await using var check = databases.NewRequest();
        var stored = await check.LoadAsync(account.Id);
        Assert.False(stored.AwaitingActivation);
        Assert.Equal(1, stored.CredentialGeneration);
        Assert.NotNull((await InvitationOfAsync(check, account.Id)).SpentAtUtc);
        Assert.Single(await EventsOfAsync(check, account.Id, AuditAction.AccountActivated));
    }

    [Fact]
    public async Task An_activation_that_commits_inside_a_resends_window_wins_and_the_resend_is_refused()
    {
        var (account, token, seed) = await SeedInvitedAccountAsync("it-race-activate-first");
        var code = await FreshCodeAsync(seed);
        using var noOther = new Barrier(participantCount: 1);
        await using var activating = databases.NewRequest();
        await using var resending = databases.NewRequest();
        ActivationOutcome? outcome = null;
        var clock = new ClockThatFirstRuns(() =>
            outcome = Activation(activating, noOther).CompleteAsync(token, Password, code, default).GetAwaiter().GetResult());

        var resendFailure = await Task.Run(() => Record.ExceptionAsync(() => Resend(resending, noOther, clock).ResendInvitationAsync(account.Id, default)));

        Assert.Equal(ActivationOutcome.Activated, outcome);
        Assert.IsType<AccountChangedException>(resendFailure);
        await using var check = databases.NewRequest();
        Assert.False((await check.LoadAsync(account.Id)).AwaitingActivation);
        var invitation = await InvitationOfAsync(check, account.Id);
        Assert.True(InvitationToken.Matches(token, invitation.TokenHash));
        Assert.NotNull(invitation.SpentAtUtc);
        Assert.Single(await EventsOfAsync(check, account.Id, AuditAction.AccountActivated));
        Assert.Empty(await EventsOfAsync(check, account.Id, AuditAction.InvitationSent));
    }

    [Fact]
    public async Task A_resend_that_commits_inside_an_activations_window_wins_and_the_activation_is_refused()
    {
        var (account, token, seed) = await SeedInvitedAccountAsync("it-race-resend-first");
        var code = await FreshCodeAsync(seed);
        using var noOther = new Barrier(participantCount: 1);
        await using var activating = databases.NewRequest();
        await using var resending = databases.NewRequest();
        var hasher = new HasherThatRuns(() =>
            Resend(resending, noOther).ResendInvitationAsync(account.Id, default).GetAwaiter().GetResult());

        var outcome = await Task.Run(() => Activation(activating, hasher).CompleteAsync(token, Password, code, default));

        Assert.Equal(ActivationOutcome.InvitationInvalid, outcome);
        await using var check = databases.NewRequest();
        Assert.True((await check.LoadAsync(account.Id)).AwaitingActivation);
        var invitation = await InvitationOfAsync(check, account.Id);
        Assert.False(InvitationToken.Matches(token, invitation.TokenHash));
        Assert.Null(invitation.SpentAtUtc);
        Assert.Empty(await EventsOfAsync(check, account.Id, AuditAction.AccountActivated));
        Assert.Single(await EventsOfAsync(check, account.Id, AuditAction.InvitationSent));
    }

    [Fact]
    public async Task An_inspection_of_the_old_link_during_a_resend_leaves_no_seed_for_the_new_link()
    {
        var (account, oldToken, _) = await SeedInvitedAccountAsync("it-race-inspect-resend", inspected: false);
        using var noOther = new Barrier(participantCount: 1);
        await using var inspecting = databases.NewRequest();
        await using var resending = databases.NewRequest();
        string? shown = null;
        // The resend reads the clock after it has loaded the invitation and before it commits.
        var clock = new ClockThatFirstRuns(() =>
            shown = Activation(inspecting, noOther).InspectAsync(oldToken, default).GetAwaiter().GetResult()?.SecretBase32);

        await Task.Run(() => Resend(resending, noOther, clock).ResendInvitationAsync(account.Id, default));

        Assert.NotNull(shown);
        await using var check = databases.NewRequest();
        var invitation = await InvitationOfAsync(check, account.Id);
        Assert.False(InvitationToken.Matches(oldToken, invitation.TokenHash));
        Assert.Null(invitation.PendingTotpSecret);
    }

    [Fact]
    public async Task Two_first_resends_of_an_account_without_an_invitation_commit_one_and_refuse_the_other()
    {
        var (account, _, _) = await SeedInvitedAccountAsync("it-race-first-resends");
        await using (var migrated = databases.NewRequest())
        {
            // An account the upgrade invited has no invitation until its first resend.
            await migrated.Context.AccountInvitations.Where(i => i.UserId == account.Id).ExecuteDeleteAsync();
        }

        using var noOther = new Barrier(participantCount: 1);
        using var bothLoaded = new Barrier(participantCount: 2);
        await using var first = databases.NewRequest();
        await using var second = databases.NewRequest();

        var failures = await Task.WhenAll(
            Task.Run(() => Record.ExceptionAsync(() => Resend(first, noOther, ClockThatWaitsForTheOther(bothLoaded)).ResendInvitationAsync(account.Id, default))),
            Task.Run(() => Record.ExceptionAsync(() => Resend(second, noOther, ClockThatWaitsForTheOther(bothLoaded)).ResendInvitationAsync(account.Id, default))));

        Assert.Single(failures, failure => failure is null);
        Assert.Single(failures, failure => failure is AccountChangedException);
        await using var check = databases.NewRequest();
        await InvitationOfAsync(check, account.Id);
        Assert.Single(await EventsOfAsync(check, account.Id, AuditAction.InvitationSent));
    }

    /// <summary>An invited account and its invitation, inspected unless told otherwise: its token and the seed it holds or would show.</summary>
    private async Task<(User Account, string Token, string Seed)> SeedInvitedAccountAsync(string username, bool inspected = true)
    {
        var account = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            DisplayName = username,
            Email = $"{username}@example.test",
            ProjectKey = "GLOBAL",
            AwaitingActivation = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        var token = InvitationToken.Generate();
        var seed = Authenticator.GenerateSecret();
        await using var request = databases.NewRequest();
        await request.Users.AddAsync(account, default);
        request.Context.AccountInvitations.Add(new AccountInvitation
        {
            Id = Guid.NewGuid(),
            UserId = account.Id,
            TokenHash = InvitationToken.Hash(token),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow + InvitationToken.Lifetime,
            PendingTotpSecret = inspected ? seed : null,
        });
        await request.Context.SaveChangesAsync();
        return (account, token, seed);
    }

    /// <summary>A code for the current step, not so close to its end that the vault reads it in the next.</summary>
    private static async Task<string> FreshCodeAsync(string seed)
    {
        if (Totp.SecondsLeftInStep() < 5)
        {
            await Task.Delay(TimeSpan.FromSeconds(Totp.SecondsLeftInStep() + 1));
        }

        return Totp.Code(seed, Totp.CurrentStep());
    }

    private static Task<AccountInvitation> InvitationOfAsync(AccountRequest request, Guid accountId)
        => request.Context.AccountInvitations.AsNoTracking().SingleAsync(invitation => invitation.UserId == accountId);

    private static Task<List<AuditLog>> EventsOfAsync(AccountRequest request, Guid accountId, AuditAction action)
        => request.Context.AuditLogs.AsNoTracking()
            .Where(row => row.Action == action && (row.UserId == accountId || row.ResourceId == accountId))
            .ToListAsync();

    private static AccountActivationService Activation(AccountRequest request, Barrier bothReady)
        => Activation(request, new HasherThatWaitsForTheOther(bothReady));

    private static AccountActivationService Activation(AccountRequest request, IPasswordHasher hasher) => new(
        new AccountInvitationRepository(request.Context),
        hasher,
        new CleanBreachScreen(),
        Authenticator,
        request.Changes,
        TimeProvider.System);

    private static UserAdminService Resend(AccountRequest request, Barrier bothReady, TimeProvider? clock = null) => new(
        request.Users,
        new AccountInvitationRepository(request.Context),
        new DeliveryThatWaitsForTheOther(bothReady),
        new UnusedSessionStore(),
        request.RecoveryCodes,
        new UnusedAuditSink(),
        request.Changes,
        new Administrator(),
        clock ?? TimeProvider.System);

    /// <summary>
    /// An activation hashes the chosen password after it has loaded the account and checked the code, and
    /// before it commits, so holding it here until the other request arrives makes both load before either commits.
    /// </summary>
    private sealed class HasherThatWaitsForTheOther(Barrier bothReady) : IPasswordHasher
    {
        public PasswordHashResult Hash(string password)
        {
            Assert.True(bothReady.SignalAndWait(TimeSpan.FromSeconds(10)), "The other request never reached its commit.");
            return new PasswordHashResult(new byte[32], new byte[16]);
        }

        public bool Verify(string password, byte[] hash, byte[] salt) => throw new NotSupportedException();
    }

    /// <summary>Runs <paramref name="interleaved"/> where an activation hashes: after it has checked the code, before it commits.</summary>
    private sealed class HasherThatRuns(Action interleaved) : IPasswordHasher
    {
        public PasswordHashResult Hash(string password)
        {
            interleaved();
            return new PasswordHashResult(new byte[32], new byte[16]);
        }

        public bool Verify(string password, byte[] hash, byte[] salt) => throw new NotSupportedException();
    }

    /// <summary>A resend asks whether it can deliver after it has loaded the account and before it commits.</summary>
    private sealed class DeliveryThatWaitsForTheOther(Barrier bothReady) : IAccountInvitationDelivery
    {
        public bool Available
        {
            get
            {
                Assert.True(bothReady.SignalAndWait(TimeSpan.FromSeconds(10)), "The other request never reached its commit.");
                return true;
            }
        }

        public Task<bool> TrySendAsync(string address, string displayName, string username, string token, CancellationToken ct)
            => Task.FromResult(false);
    }

    /// <summary>A resend reads the clock after it has loaded the invitation, so both resends load before either commits.</summary>
    private static ClockThatFirstRuns ClockThatWaitsForTheOther(Barrier bothLoaded) => new(() =>
        Assert.True(bothLoaded.SignalAndWait(TimeSpan.FromSeconds(10)), "The other request never reached its commit."));

    /// <summary>Runs <paramref name="interleaved"/> the first time the time is read, then tells the real time.</summary>
    private sealed class ClockThatFirstRuns(Action interleaved) : TimeProvider
    {
        private Action? _pending = interleaved;

        public override DateTimeOffset GetUtcNow()
        {
            Interlocked.Exchange(ref _pending, null)?.Invoke();
            return TimeProvider.System.GetUtcNow();
        }
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

    /// <summary>A resend is recorded in its own commit, never written to the sink on its own.</summary>
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
public sealed class SqlServerAccountActivationRaceTests(SqlServerAccountChangeDatabases databases) : AccountActivationRaceTests(databases);

[Collection(PostgresAccountChangeCollection.Name)]
public sealed class PostgresAccountActivationRaceTests(PostgresAccountChangeDatabases databases) : AccountActivationRaceTests(databases);
