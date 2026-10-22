using System.Text;
using EclipsVault.Core.Application.Abstractions;
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
/// A password change is a credential reset, so it must turn out every other session: a session
/// opened with the old password cannot be allowed to outlive it. These pin that the account-wide
/// revocation fires on success — and, crucially, only on success — so a rejected change (wrong
/// current password, breached new one) never disturbs a live session.
/// </summary>
public class ProfileServiceTests
{
    private const string CurrentPassword = "current-password-value";
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed class RecordingRevocation : ISessionRevocationService
    {
        public List<(Guid UserId, DateTimeOffset At)> Revocations { get; } = [];

        public Task RevokeAsync(Guid userId, DateTimeOffset revokedAtUtc, CancellationToken ct = default)
        {
            Revocations.Add((userId, revokedAtUtc));
            return Task.CompletedTask;
        }

        public Task<bool> IsRevokedAsync(Guid userId, DateTimeOffset sessionIssuedAtUtc, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

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
        public int Updates { get; private set; }

        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<User?>(user);
        public Task<bool> IsEmailTakenAsync(string email, Guid accountId, CancellationToken ct) => Task.FromResult(emailTaken);

        public Task UpdateAsync(User user, CancellationToken ct)
        {
            Updates++;
            return Task.CompletedTask;
        }

        public Task<User?> FindByEmailAsync(string email, CancellationToken ct) => throw new NotSupportedException();

        public Task<User?> FindByUsernameAsync(string username, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindByUsernameOrEmailAsync(string identifier, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> FindEmailsWithPrefixAsync(string localPrefix, string domain, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task SetAvatarAsync(User user, byte[] png, CancellationToken ct) => throw new NotSupportedException();
        public Task RemoveAvatarAsync(User user, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class NoopNotifications : INotificationService
    {
        public Task NotifyPasswordChangedAsync(Guid userId, CancellationToken ct) => Task.CompletedTask;
        public Task NotifyAccessRequestDecidedAsync(Guid requesterUserId, string secretName, bool approved, string reviewer, string? note, CancellationToken ct) => throw new NotSupportedException();
        public Task NotifyUserProvisionedAsync(string email, string displayName, string username, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> NotifyExpiringSecretAsync(Guid ownerUserId, string secretName, DateTimeOffset expiresAtUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<EmailLogDto>> ListRecentAsync(int max, CancellationToken ct) => throw new NotSupportedException();
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
        public Task ReplaceAllAsync(Guid userId, IReadOnlyList<MfaRecoveryCode> codes, CancellationToken ct) => throw new NotSupportedException();
        public Task MarkUsedAsync(MfaRecoveryCode code, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAllAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
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

    private static (ProfileService Service, RecordingRevocation Revocation) NewService(bool breached = false)
        => NewService(new StubUsers(Owner(), emailTaken: false), breached);

    private static (ProfileService Service, RecordingRevocation Revocation) NewService(StubUsers users, bool breached = false)
    {
        var revocation = new RecordingRevocation();
        var service = new ProfileService(
            users,
            new StubHasher(),
            new ThrowingAvatarProcessor(),
            new ThrowingRecoveryCodes(),
            new StubBreachScreen(breached),
            new NoopNotifications(),
            revocation,
            new NoopSink(),
            new StubActor(),
            TimeProvider.System);

        return (service, revocation);
    }

    [Fact]
    public async Task Changing_the_password_revokes_every_session_for_the_user()
    {
        var (service, revocation) = NewService();

        await service.ChangePasswordAsync(UserId, CurrentPassword, "a-brand-new-password", CancellationToken.None);

        var revoked = Assert.Single(revocation.Revocations);
        Assert.Equal(UserId, revoked.UserId);
    }

    [Fact]
    public async Task A_wrong_current_password_changes_nothing_and_revokes_nothing()
    {
        var (service, revocation) = NewService();

        await Assert.ThrowsAsync<ProfileException>(() =>
            service.ChangePasswordAsync(UserId, "not-the-current-password", "a-brand-new-password", CancellationToken.None));

        Assert.Empty(revocation.Revocations); // a rejected change must not disturb a live session
    }

    [Fact]
    public async Task A_breached_new_password_is_rejected_without_revoking()
    {
        var (service, revocation) = NewService(breached: true);

        await Assert.ThrowsAsync<ProfileException>(() =>
            service.ChangePasswordAsync(UserId, CurrentPassword, "a-brand-new-password", CancellationToken.None));

        Assert.Empty(revocation.Revocations);
    }

    [Fact]
    public async Task An_email_another_account_signs_in_with_is_refused_without_naming_it_or_saving()
    {
        var users = new StubUsers(Owner(), emailTaken: true);
        var (service, _) = NewService(users);

        var refused = await Assert.ThrowsAsync<EmailAddressTakenException>(() =>
            service.UpdateAsync(UserId, "Owner", "taken@example.test", CancellationToken.None));

        Assert.Equal("That email address is already used by another account.", refused.Message);
        Assert.Equal(0, users.Updates);
    }
}
