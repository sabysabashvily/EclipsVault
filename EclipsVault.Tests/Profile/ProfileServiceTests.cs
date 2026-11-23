using System.Text;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Profile;
using EclipsVault.Core.Application.Sessions;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using Xunit;

namespace EclipsVault.Tests.Profile;

public class ProfileServiceTests
{
    private const string CurrentPassword = "current-password-value";
    private static readonly Guid UserId = Guid.NewGuid();

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
        public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> AnyOtherEnabledAdministratorAsync(Guid accountId, CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public void StageAddition(User user) => throw new NotSupportedException();
        public void StageRemoval(User user) => throw new NotSupportedException();
        public Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task StageAvatarAsync(User user, byte[] png, CancellationToken ct) => throw new NotSupportedException();
        public Task StageAvatarRemovalAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryAcceptTotpStepAsync(User user, long step, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryCompleteTotpEnrollmentAsync(User user, long step, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> RecordFailedAttemptAsync(User user, LockoutPolicy lockout, DateTimeOffset nowUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearFailedAttemptsAsync(User user, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingChanges : IAccountChangeCommitter
    {
        public Task<bool> CommitAsync(User account, AuditEntry entry, CancellationToken ct)
            => CommitAsync(account, entry.Action, entry.Details!, ct);

        public Task<bool> CommitAsync(User account, AuditEntry entry, Func<CancellationToken, Task> recheck, CancellationToken ct)
            => throw new NotSupportedException();

        public List<(AuditAction Action, long CredentialGeneration)> Committed { get; } = [];

        public Task<bool> CommitAsync(User account, AuditAction action, string details, CancellationToken ct)
        {
            Committed.Add((action, account.CredentialGeneration));
            return Task.FromResult(true);
        }

        public Task<bool> CommitAsync(User account, AuditAction action, string details, Func<CancellationToken, Task> recheck, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<bool> CommitAdministratorChangeAsync(User account, AuditEntry entry, Func<CancellationToken, Task> recheck, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class ThrowingAvatarProcessor : IAvatarProcessor
    {
        public int MaxUploadBytes => 0;
        public byte[] ProcessToPng(byte[] uploadedBytes) => throw new NotSupportedException();
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

    private static (ProfileService Service, RecordingChanges Changes) NewService(bool emailTaken = false)
    {
        var changes = new RecordingChanges();
        var service = new ProfileService(new StubUsers(Owner(), emailTaken), new ThrowingAvatarProcessor(), changes);

        return (service, changes);
    }

    // A password change is prepared, verified and then committed by the verified Profile services
    // (ADR-0022). Preparation refuses what the old one-step change refused; a wrong current password
    // is ProfileVerificationTests', and the generation the commit advances is CredentialGenerationTests'.
    private static ProfileOperationPreparation Preparation(bool breached = false)
        => new(new StubUsers(Owner(), emailTaken: false), null!, null!, null!, new StubBreachScreen(breached), null!, TimeProvider.System, null!);

    private static ProfileChangeRequest NewPassword => new(ProfileOperationKind.ChangePassword, "a-brand-new-password", null, null);

    [Fact]
    public async Task A_breached_new_password_is_refused_before_anything_is_prepared()
    {
        var refused = await Assert.ThrowsAsync<ProfileOperationRefusedException>(() => Preparation(breached: true)
            .PrepareAsync(new ProfileSession(UserId, Guid.NewGuid(), new SessionStamp(0, "current")), NewPassword, CancellationToken.None));

        Assert.Equal(ProfileRefusal.InvalidInput, refused.Refusal);
        Assert.Equal("newPassword", refused.Field);
    }

    [Fact]
    public async Task A_password_change_from_a_session_of_an_earlier_generation_is_refused()
    {
        var refused = await Assert.ThrowsAsync<ProfileOperationRefusedException>(() => Preparation()
            .PrepareAsync(new ProfileSession(UserId, Guid.NewGuid(), new SessionStamp(3, "current")), NewPassword, CancellationToken.None));

        Assert.Equal(ProfileRefusal.OperationChanged, refused.Refusal);
    }

    [Fact]
    public async Task Saving_a_display_name_preserves_the_email_and_credential_generation()
    {
        var (service, changes) = NewService();

        Assert.Equal("New Owner", await service.UpdateDisplayNameAsync(UserId, "  New Owner  ", default));

        var profile = await service.GetAsync(UserId, default);
        Assert.Equal("owner@example.test", profile!.Email);
        Assert.Equal([(AuditAction.ProfileUpdated, 0L)], changes.Committed);
    }

    [Fact]
    public async Task An_empty_display_name_commits_nothing()
    {
        var (service, changes) = NewService();

        await Assert.ThrowsAsync<ProfileException>(() => service.UpdateDisplayNameAsync(UserId, "  ", default));

        Assert.Empty(changes.Committed);
    }

}
