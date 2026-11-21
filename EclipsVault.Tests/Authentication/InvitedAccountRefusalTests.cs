using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Activity;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Sessions;
using EclipsVault.Core.Application.Sso;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Models;
using Xunit;

namespace EclipsVault.Tests.Authentication;

public class InvitedAccountRefusalTests
{
    private static readonly SessionStamp MatchingStamp = new(4, "rev-1");

    [Fact]
    public async Task Password_stage_hashes_before_refusing_an_invited_account_and_records_awaiting_activation()
    {
        var invited = InvitedAccount(passwordHash: [], passwordSalt: []);
        var hasher = new CountingHasher();
        var attempt = new RecordingAttempt();
        var auth = AuthService(invited, hasher, attempt);

        var result = await auth.ValidateCredentialsAsync(invited.Username, "Any-password-123!", default);

        Assert.Equal(CredentialStatus.Invalid, result.Status);
        Assert.Equal(1, hasher.VerifyCalls);
        Assert.Equal(32, hasher.LastHashLength);
        Assert.Equal(16, hasher.LastSaltLength);
        var entry = Assert.Single(attempt.Entries);
        Assert.Equal(AuditAction.LoginFailed, entry.Action);
        Assert.Equal("Account awaiting activation", entry.Details);
    }

    [Fact]
    public async Task Pending_second_factor_and_enrollment_refuse_an_invited_account_even_with_a_matching_stamp()
    {
        var invited = InvitedAccount(totpEnabled: true, totpSecret: "JBSWY3DPEHPK3PXP");
        var auth = AuthService(invited, new CountingHasher(), new RecordingAttempt());

        await Assert.ThrowsAsync<StaleProofException>(() => auth.VerifyTotpAsync(invited.Id, MatchingStamp, "123456", default));
        await Assert.ThrowsAsync<StaleProofException>(() => auth.VerifyRecoveryCodeAsync(invited.Id, MatchingStamp, "CODE-1234", default));
        await Assert.ThrowsAsync<StaleProofException>(() => auth.BeginTotpEnrollmentAsync(invited.Id, MatchingStamp, default));
        await Assert.ThrowsAsync<StaleProofException>(() => auth.CompleteTotpEnrollmentAsync(invited.Id, MatchingStamp, "123456", default));
    }

    [Fact]
    public async Task Break_glass_refuses_an_invited_administrator_even_with_a_matching_stamp()
    {
        var invitedAdmin = InvitedAccount(clearance: ClearanceLevel.TopSecret, totpEnabled: true, totpSecret: "JBSWY3DPEHPK3PXP");
        var attempt = new RecordingAttempt();
        var auth = AuthService(invitedAdmin, new CountingHasher(), attempt);

        var verified = await auth.VerifyBreakGlassAsync(invitedAdmin.Id, MatchingStamp, "123456", default);

        Assert.Null(verified);
        var entry = Assert.Single(attempt.Entries);
        Assert.Equal(AuditAction.LoginFailed, entry.Action);
        Assert.Equal("Account awaiting activation", entry.Details);
    }

    [Fact]
    public async Task Sso_refuses_an_invited_account_with_NotActivated_and_audits_the_refusal()
    {
        var invited = InvitedAccount();
        var audit = new RecordingAuditSink();
        var sso = new SsoSignInService(new SingleUserRepository(invited), audit, new FixedRevocation("rev-1"), new SsoPolicy(TrustIdpMultiFactor: true));

        var decision = await sso.SignInAsync(new ExternalIdentity(
            Email: invited.Email,
            EmailVerified: true,
            Issuer: "https://idp.example.test",
            Subject: "sub-invited",
            AuthenticationMethods: ["mfa"]), default);

        Assert.Equal(SsoOutcome.NotActivated, decision.Outcome);
        Assert.Null(decision.User);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditAction.SsoSignInRefused, entry.Action);
        Assert.Contains("NotActivated", entry.Details!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AuditAction.InvitationSent, ActivityCategory.Administration, "Sent an account invitation", "Invitation sent")]
    [InlineData(AuditAction.InvitationAddressReplaced, ActivityCategory.Administration, "Replaced an invitation address", "Invitation address replaced")]
    [InlineData(AuditAction.AccountActivated, ActivityCategory.Account, "Activated your account", "Account activated")]
    public void Invitation_audit_actions_have_explicit_activity_and_audit_display_labels(
        AuditAction action, ActivityCategory category, string activityTitle, string auditLabel)
    {
        var activity = ActivityDescriber.Describe(action);
        var (label, _) = AuditDisplay.For(action);

        Assert.Equal(category, activity.Category);
        Assert.Equal(ActivitySeverity.Notable, activity.Severity);
        Assert.Equal(activityTitle, activity.Title);
        Assert.Equal(auditLabel, label);
    }

    private static VaultAuthenticationService AuthService(User user, IPasswordHasher hasher, IAuthenticationAttempt attempt)
    {
        var users = new SingleUserRepository(user);
        var totp = new UnusedTotp();
        var recovery = new UnusedRecoveryCodes();
        var secondFactors = new SecondFactorVerifier(users, recovery, totp, hasher, TimeProvider.System);
        var failures = new AuthenticationFailures(users, attempt, LockoutPolicy.Default, TimeProvider.System);
        return new VaultAuthenticationService(
            users, hasher, totp, recovery, secondFactors, failures,
            new RecordingAuditSink(), attempt, new FixedRevocation("rev-1"), TimeProvider.System);
    }

    private static User InvitedAccount(
        ClearanceLevel clearance = ClearanceLevel.Standard,
        bool totpEnabled = false,
        string? totpSecret = null,
        byte[]? passwordHash = null,
        byte[]? passwordSalt = null) => new()
        {
            Id = Guid.NewGuid(),
            Username = "invited-user",
            DisplayName = "Invited User",
            Email = "invited@example.test",
            PasswordHash = passwordHash ?? new byte[32],
            PasswordSalt = passwordSalt ?? new byte[16],
            Clearance = clearance,
            ProjectKey = "GLOBAL",
            TotpEnabled = totpEnabled,
            TotpSecret = totpSecret,
            CredentialGeneration = MatchingStamp.CredentialGeneration,
            AwaitingActivation = true
        };

    private sealed class CountingHasher : IPasswordHasher
    {
        public int VerifyCalls { get; private set; }
        public int LastHashLength { get; private set; }
        public int LastSaltLength { get; private set; }

        public PasswordHashResult Hash(string password) => throw new NotSupportedException();

        public bool Verify(string password, byte[] hash, byte[] salt)
        {
            VerifyCalls++;
            LastHashLength = hash.Length;
            LastSaltLength = salt.Length;
            return true;
        }
    }

    private sealed class RecordingAttempt : IAuthenticationAttempt
    {
        public List<AuditEntry> Entries { get; } = [];
        public Task<T> RunAsync<T>(User account, Func<CancellationToken, Task<T>> step, CancellationToken ct) => step(ct);
        public void Record(AuditEntry entry) => Entries.Add(entry);
    }

    private sealed class RecordingAuditSink : IAuditSink
    {
        public List<AuditEntry> Entries { get; } = [];
        public Task WriteAsync(AuditEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedRevocation(string version) : ISessionRevocationService
    {
        public Task RevokeAsync(Guid userId, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> FindAsync(Guid userId, CancellationToken ct) => Task.FromResult<string?>(version);
        public Task<string> CaptureAsync(Guid userId, CancellationToken ct) => Task.FromResult(version);
    }

    private sealed class UnusedTotp : ITotpService
    {
        public string GenerateSecret() => throw new NotSupportedException();
        public bool TryValidateCode(string secretBase32, string code, long? lastUsedStep, out long matchedStep)
            => throw new NotSupportedException();
        public string BuildOtpAuthUri(string secretBase32, string accountName) => throw new NotSupportedException();
    }

    private sealed class UnusedRecoveryCodes : IMfaRecoveryCodeRepository
    {
        public Task<IReadOnlyList<MfaRecoveryCode>> ListUnusedAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CountUnusedAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task StageReplacementAsync(Guid userId, IReadOnlyList<MfaRecoveryCode> codes, CancellationToken ct) => throw new NotSupportedException();
        public Task StageRemovalOfAllAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryRedeemAsync(MfaRecoveryCode code, DateTimeOffset usedAtUtc, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class SingleUserRepository(User user) : IUserRepository
    {
        public Task<User?> FindByUsernameAsync(string username, CancellationToken ct) => Task.FromResult(user.Username == username ? user : null);
        public Task<User?> FindByUsernameOrEmailAsync(string identifier, CancellationToken ct) => Task.FromResult(user.Username == identifier ? user : null);
        public Task<User?> FindByEmailAsync(string email, CancellationToken ct) => Task.FromResult(user.Email == email ? user : null);
        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(user.Id == id ? user : null);
        public Task<bool> ReloadAsync(User account, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> IsEmailTakenAsync(string email, Guid accountId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> FindEmailsWithPrefixAsync(string localPrefix, string domain, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> AnyOtherEnabledAdministratorAsync(Guid accountId, CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(User account, CancellationToken ct) => throw new NotSupportedException();
        public Task UpdateAsync(User account, CancellationToken ct) => throw new NotSupportedException();
        public void StageAddition(User account) => throw new NotSupportedException();
        public void StageRemoval(User account) => throw new NotSupportedException();
        public Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task StageAvatarAsync(User account, byte[] png, CancellationToken ct) => throw new NotSupportedException();
        public Task StageAvatarRemovalAsync(User account, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryAcceptTotpStepAsync(User account, long step, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryCompleteTotpEnrollmentAsync(User account, long step, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> RecordFailedAttemptAsync(User account, LockoutPolicy lockout, DateTimeOffset nowUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearFailedAttemptsAsync(User account, CancellationToken ct) => throw new NotSupportedException();
    }
}
