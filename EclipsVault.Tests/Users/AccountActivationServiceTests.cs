using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using Xunit;

namespace EclipsVault.Tests.Users;

/// <summary>
/// Completing an activation changes the account and its invitation in memory, then hands everything to the
/// account committer in one call, so the password, the authenticator, the spent token, the new credential
/// generation and <c>AccountActivated</c> commit together or not at all.
/// </summary>
public class AccountActivationServiceTests
{
    private const string Token = "activation-token";
    private const string Seed = "PENDINGSEED";
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Completing_an_activation_commits_every_change_in_one_call_with_AccountActivated()
    {
        var (account, invitation) = InvitedAccount();
        var committer = new RecordingCommitter(invitation);

        var outcome = await Service(invitation, committer).CompleteAsync(Token, "A-new-password-123", "123456", default);

        Assert.Equal(ActivationOutcome.Activated, outcome);
        var commit = Assert.Single(committer.Commits);
        Assert.Equal(new Commit(
            AuditAction.AccountActivated, account.Id,
            AwaitingActivation: false, TotpEnabled: true, TotpSecret: Seed, LastTotpStep: 42, PasswordHash: "07",
            CredentialGeneration: 5, SpentAtUtc: Now, PendingTotpSecret: null), commit);
    }

    [Fact]
    public async Task An_activation_whose_audit_row_cannot_be_written_is_not_reported_as_activated()
    {
        var (_, invitation) = InvitedAccount();
        var committer = new RecordingCommitter(invitation) { Failure = new AuditWriteFailedException("The trail refused the row.") };

        await Assert.ThrowsAsync<AuditWriteFailedException>(
            () => Service(invitation, committer).CompleteAsync(Token, "A-new-password-123", "123456", default));

        Assert.Single(committer.Commits);
    }

    private static (User Account, AccountInvitation Invitation) InvitedAccount()
    {
        var account = new User
        {
            Id = Guid.NewGuid(),
            Username = "invited",
            DisplayName = "Invited Person",
            Email = "invited@example.test",
            ProjectKey = "GLOBAL",
            AwaitingActivation = true,
            CredentialGeneration = 4,
        };
        var invitation = new AccountInvitation
        {
            Id = Guid.NewGuid(),
            UserId = account.Id,
            User = account,
            TokenHash = InvitationToken.Hash(Token),
            CreatedAtUtc = Now.AddHours(-1),
            ExpiresAtUtc = Now.AddHours(71),
            PendingTotpSecret = Seed,
        };
        return (account, invitation);
    }

    private static AccountActivationService Service(AccountInvitation invitation, RecordingCommitter committer) => new(
        new OneInvitation(invitation), new FixedHasher(), new CleanBreachScreen(), new AcceptingTotp(), committer, new StoppedClock(Now));

    /// <summary>What the account and its invitation held when the committer was called.</summary>
    private sealed record Commit(
        AuditAction Action, Guid AccountId, bool AwaitingActivation, bool TotpEnabled, string? TotpSecret, long? LastTotpStep,
        string PasswordHash, long CredentialGeneration, DateTimeOffset? SpentAtUtc, string? PendingTotpSecret);

    private sealed class RecordingCommitter(AccountInvitation invitation) : IAccountChangeCommitter
    {
        public List<Commit> Commits { get; } = [];
        public Exception? Failure { get; init; }

        public Task<bool> CommitAsync(User account, AuditAction action, string details, CancellationToken ct)
        {
            Commits.Add(new Commit(action, account.Id, account.AwaitingActivation, account.TotpEnabled, account.TotpSecret,
                account.LastTotpStep, Convert.ToHexString(account.PasswordHash), account.CredentialGeneration,
                invitation.SpentAtUtc, invitation.PendingTotpSecret));
            return Failure is null ? Task.FromResult(true) : Task.FromException<bool>(Failure);
        }

        public Task<bool> CommitAsync(User account, AuditAction action, string details, Func<CancellationToken, Task> recheck, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<bool> CommitAsync(User account, AuditEntry entry, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CommitAsync(User account, AuditEntry entry, Func<CancellationToken, Task> recheck, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<bool> CommitAdministratorChangeAsync(User account, AuditEntry entry, Func<CancellationToken, Task> recheck, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class OneInvitation(AccountInvitation invitation) : IAccountInvitationRepository
    {
        public Task<AccountInvitation?> FindByTokenHashAsync(byte[] tokenHash, CancellationToken ct)
            => Task.FromResult<AccountInvitation?>(tokenHash.SequenceEqual(invitation.TokenHash) ? invitation : null);

        public Task<AccountInvitation?> FindByUserIdAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccountInvitation>> ListAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> StorePendingSecretOnceAsync(Guid invitationId, byte[] tokenHash, string seed, DateTimeOffset nowUtc, CancellationToken ct)
            => throw new NotSupportedException();
        public void StageAddition(AccountInvitation invitation) => throw new NotSupportedException();
        public void StageReset(AccountInvitation invitation) => throw new NotSupportedException();
    }

    private sealed class FixedHasher : IPasswordHasher
    {
        public PasswordHashResult Hash(string password) => new([7], [9]);
        public bool Verify(string password, byte[] hash, byte[] salt) => throw new NotSupportedException();
    }

    private sealed class CleanBreachScreen : IBreachedPasswordScreen
    {
        public bool IsCompromised(string password) => false;
        public int CorpusSize => 0;
    }

    private sealed class AcceptingTotp : ITotpService
    {
        public string GenerateSecret() => throw new NotSupportedException();

        public bool TryValidateCode(string secretBase32, string code, long? lastUsedStep, out long matchedStep)
        {
            matchedStep = 42;
            return secretBase32 == Seed && code == "123456";
        }

        public string BuildOtpAuthUri(string secretBase32, string accountName) => throw new NotSupportedException();
    }

    private sealed class StoppedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
