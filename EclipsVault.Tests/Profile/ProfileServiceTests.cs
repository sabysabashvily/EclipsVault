using System.Text;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Mfa;
using EclipsVault.Core.Application.Notifications;
using EclipsVault.Core.Application.Profile;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using Xunit;

namespace EclipsVault.Tests.Profile;

/// <summary>
/// Changing how an account signs in must end every session the old credentials opened, so the change
/// advances the credential generation in the same commit. These pin that it does on success, only on
/// success, and only for a real sign-in change: a rejected change or a new display name leaves every
/// live session alone.
/// </summary>
public class ProfileServiceTests
{
    private const string CurrentPassword = "current-password-value";
    private static readonly Guid UserId = Guid.NewGuid();

    // Verifies the fixed "current-password" bytes and nothing else, so a test can drive the
    // wrong-password branch by passing anything but CurrentPassword.
    private sealed class StubHasher : IPasswordHasher
    {
        private static readonly byte[] CurrentHash = Encoding.UTF8.GetBytes(CurrentPassword);

        public PasswordHashResult Hash(string password) => new(Encoding.UTF8.GetBytes(password), [1, 2, 3]);

        public bool Verify(string password, byte[] hash, byte[] salt)
            => hash.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(password)) && hash.AsSpan().SequenceEqual(CurrentHash);
    }

    private sealed class StubBreachScreen(bool compromised) : IBreachedPasswordScreen
    {
        public bool IsCompromised(string password) => compromised;
        public int CorpusSize => 0;
    }

    private sealed class StubUsers(User user, bool emailTaken) : IUserRepository
    {
        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<User?>(user);
        public Task<bool> ReloadAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsEmailTakenAsync(string email, Guid accountId, CancellationToken ct) => Task.FromResult(emailTaken);

        public Task UpdateAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindByEmailAsync(string email, CancellationToken ct) => throw new NotSupportedException();

        public Task<User?> FindByUsernameAsync(string username, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindByUsernameOrEmailAsync(string identifier, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> FindEmailsWithPrefixAsync(string localPrefix, string domain, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task StageAvatarAsync(User user, byte[] png, CancellationToken ct) => throw new NotSupportedException();
        public Task StageAvatarRemovalAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryAcceptTotpStepAsync(User user, long step, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryCompleteTotpEnrollmentAsync(User user, long step, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> RecordFailedAttemptAsync(User user, LockoutPolicy lockout, DateTimeOffset nowUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearFailedAttemptsAsync(User user, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class NoopNotifications : INotificationService
    {
        public Task NotifyPasswordChangedAsync(Guid userId, CancellationToken ct) => Task.CompletedTask;
        public Task NotifyAccessRequestDecidedAsync(Guid requesterUserId, string secretName, bool approved, string reviewer, string? note, CancellationToken ct) => throw new NotSupportedException();
        public Task NotifyUserProvisionedAsync(string email, string displayName, string username, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> NotifyExpiringSecretAsync(Guid ownerUserId, string secretName, DateTimeOffset expiresAtUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<EmailLogDto>> ListRecentAsync(int max, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingChanges : IAccountChangeCommitter
    {
        public Task<bool> CommitAsync(User account, AuditEntry entry, CancellationToken ct)
            => CommitAsync(account, entry.Action, entry.Details!, ct);

        public List<(AuditAction Action, long CredentialGeneration)> Committed { get; } = [];

        public Task<bool> CommitAsync(User account, AuditAction action, string details, CancellationToken ct)
        {
            Committed.Add((action, account.CredentialGeneration));
            return Task.FromResult(true);
        }
    }

    private sealed class NoopSink : IAuditSink
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubActor : IAuditContext
    {
        public Guid? UserId => ProfileServiceTests.UserId;
        public string? Username => "owner";
        public string? SourceIp => "::1";
    }

    private sealed class ThrowingAvatarProcessor : IAvatarProcessor
    {
        public int MaxUploadBytes => 0;
        public byte[] ProcessToPng(byte[] uploadedBytes) => throw new NotSupportedException();
    }

    private sealed class ThrowingRecoveryCodes : IMfaRecoveryCodeRepository
    {
        public Task<IReadOnlyList<MfaRecoveryCode>> ListUnusedAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CountUnusedAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task StageReplacementAsync(Guid userId, IReadOnlyList<MfaRecoveryCode> codes, CancellationToken ct) => throw new NotSupportedException();
        public Task StageRemovalOfAllAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryRedeemAsync(MfaRecoveryCode code, DateTimeOffset usedAtUtc, CancellationToken ct) => throw new NotSupportedException();
    }

    private static User Owner() => new()
    {
        Id = UserId,
        Username = "owner",
        Email = "owner@example.test",
        PasswordHash = Encoding.UTF8.GetBytes(CurrentPassword),
        PasswordSalt = [9, 9, 9],
        Clearance = ClearanceLevel.Standard
    };

    private static (ProfileService Service, RecordingChanges Changes) NewService(
        bool breached = false, bool emailTaken = false)
    {
        var changes = new RecordingChanges();
        var service = new ProfileService(
            new StubUsers(Owner(), emailTaken),
            new StubHasher(),
            new ThrowingAvatarProcessor(),
            new ThrowingRecoveryCodes(),
            new StubBreachScreen(breached),
            new NoopNotifications(),
            changes,
            new NoopSink(),
            new StubActor());

        return (service, changes);
    }

    [Fact]
    public async Task Changing_the_password_advances_the_credential_generation_in_the_same_commit()
    {
        var (service, changes) = NewService();

        var committed = await service.ChangePasswordAsync(UserId, 0, CurrentPassword, "a-brand-new-password", CancellationToken.None);

        Assert.Equal(1, committed);
        Assert.Equal([(AuditAction.PasswordChanged, 1L)], changes.Committed);
    }

    [Fact]
    public async Task A_wrong_current_password_commits_nothing()
    {
        var (service, changes) = NewService();

        await Assert.ThrowsAsync<ProfileException>(() =>
            service.ChangePasswordAsync(UserId, 0, "not-the-current-password", "a-brand-new-password", CancellationToken.None));

        Assert.Empty(changes.Committed);
    }

    [Fact]
    public async Task A_breached_new_password_is_rejected_without_committing()
    {
        var (service, changes) = NewService(breached: true);

        await Assert.ThrowsAsync<ProfileException>(() =>
            service.ChangePasswordAsync(UserId, 0, CurrentPassword, "a-brand-new-password", CancellationToken.None));

        Assert.Empty(changes.Committed);
    }

    [Fact]
    public async Task A_password_change_from_a_session_of_an_earlier_generation_is_refused()
    {
        var (service, changes) = NewService();

        await Assert.ThrowsAsync<StaleProofException>(() =>
            service.ChangePasswordAsync(UserId, 3, CurrentPassword, "a-brand-new-password", CancellationToken.None));

        Assert.Empty(changes.Committed);
    }

    [Fact]
    public async Task Changing_the_email_advances_the_credential_generation()
    {
        var (service, changes) = NewService();

        var update = await service.UpdateAsync(UserId, 0, "Owner", "new-owner@example.test", CancellationToken.None);

        Assert.True(update.EmailChanged);
        Assert.Equal(1, update.CredentialGeneration);
        Assert.Equal([(AuditAction.ProfileUpdated, 1L)], changes.Committed);
    }

    [Fact]
    public async Task Changing_only_the_display_name_keeps_the_credential_generation()
    {
        var (service, changes) = NewService();

        var update = await service.UpdateAsync(UserId, 0, "A new display name", "owner@example.test", CancellationToken.None);

        Assert.False(update.EmailChanged);
        Assert.Equal([(AuditAction.ProfileUpdated, 0L)], changes.Committed);
    }

    [Fact]
    public async Task Respelling_the_email_in_other_letter_case_keeps_the_credential_generation()
    {
        var (service, changes) = NewService();

        var update = await service.UpdateAsync(UserId, 0, "Owner", "Owner@Example.TEST", CancellationToken.None);

        Assert.False(update.EmailChanged);
        Assert.Equal([(AuditAction.ProfileUpdated, 0L)], changes.Committed);
    }

    [Fact]
    public async Task An_email_another_account_signs_in_with_is_refused_without_naming_it_or_saving()
    {
        var (service, changes) = NewService(emailTaken: true);

        var refused = await Assert.ThrowsAsync<EmailAddressTakenException>(() =>
            service.UpdateAsync(UserId, 0, "Owner", "taken@example.test", CancellationToken.None));

        Assert.Equal("That email address is already used by another account.", refused.Message);
        Assert.Empty(changes.Committed);
    }
}
