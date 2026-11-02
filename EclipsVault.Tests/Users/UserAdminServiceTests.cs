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

namespace EclipsVault.Tests.Users;

/// <summary>
/// An administrator's change to an account reaches the database once, with its one audit event, and
/// ends sessions through the credential generation rather than the session store. Each test reads
/// what reached the vault's ports, in order, as one line per step.
/// </summary>
public class UserAdminServiceTests
{
    private static readonly Guid AdministratorId = Guid.Parse("ad000000-0000-0000-0000-000000000001");
    private static readonly Guid TargetId = Guid.Parse("7a000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Creating_an_account_commits_it_with_one_UserCreated_event_then_emails_the_new_person()
    {
        var vault = new Vault();

        var created = await vault.Service.CreateAsync(NewPerson("it-new-person"), default);

        Assert.Equal(["stage new it-new-person", "commit UserCreated (generation 0)", "email new.person.1@example.test"], vault.Steps);
        Assert.Equal("new.person.1@example.test", created.Email);
    }

    [Fact]
    public async Task A_create_whose_commit_fails_emails_no_one_and_reports_the_audit_trail_unavailable()
    {
        var vault = new Vault();
        vault.Changes.Failure = new AuditWriteFailedException("rejected");

        await Assert.ThrowsAsync<AuditWriteFailedException>(() => vault.Service.CreateAsync(NewPerson("it-unrecorded"), default));

        Assert.Equal(["stage new it-unrecorded"], vault.Steps);
    }

    [Fact]
    public async Task A_create_whose_provisioning_email_can_be_neither_sent_nor_recorded_still_creates_the_account()
    {
        var mail = new BrokenMail();
        var vault = new Vault(new NotificationService(mail, mail, new FakeUsers([], []), NotificationOptions.Default, TimeProvider.System));

        var created = await vault.Service.CreateAsync(NewPerson("it-unmailed"), default);

        Assert.Equal(["stage new it-unmailed", "commit UserCreated (generation 0)"], vault.Steps);
        Assert.Equal("new.person.1@example.test", created.Email);
    }

    [Fact]
    public async Task A_create_that_loses_the_username_race_answers_the_checks_conflict_and_emails_no_one()
    {
        var vault = new Vault();
        vault.Changes.Failure = new UsernameTakenException();

        var conflict = await Assert.ThrowsAsync<VaultAdminException>(() => vault.Service.CreateAsync(NewPerson("it-raced"), default));

        Assert.Equal("The username 'it-raced' is already taken.", conflict.Message);
        Assert.Equal(["stage new it-raced"], vault.Steps);
    }

    [Fact]
    public async Task A_create_that_loses_its_address_to_the_same_username_answers_the_username_conflict()
    {
        var vault = new Vault();
        vault.Changes.SavedFirstByAnother = new User { Id = Guid.NewGuid(), Username = "it-raced", Email = "new.person.1@example.test" };
        vault.Changes.Failure = new EmailAddressTakenException();

        var conflict = await Assert.ThrowsAsync<VaultAdminException>(() => vault.Service.CreateAsync(NewPerson("it-raced"), default));

        Assert.Equal("The username 'it-raced' is already taken.", conflict.Message);
    }

    [Fact]
    public async Task A_create_that_loses_only_its_address_reports_the_address_conflict()
    {
        var vault = new Vault();
        vault.Changes.SavedFirstByAnother = new User { Id = Guid.NewGuid(), Username = "it-other-person", Email = "new.person.1@example.test" };
        vault.Changes.Failure = new EmailAddressTakenException();

        await Assert.ThrowsAsync<EmailAddressTakenException>(() => vault.Service.CreateAsync(NewPerson("it-raced"), default));
    }

    [Fact]
    public async Task Changing_a_role_commits_one_UserRoleChanged_event_with_the_generation_advanced_and_no_store_call()
    {
        var target = Target(ClearanceLevel.TopSecret);
        var vault = new Vault(target, Administrator());

        Assert.True(await vault.Service.SetRoleAsync(TargetId, ClearanceLevel.Standard, " payments ", default));

        Assert.Equal(["commit UserRoleChanged (generation 1)"], vault.Steps);
        Assert.Equal(ClearanceLevel.Standard, target.Clearance);
        Assert.Equal("PAYMENTS", target.ProjectKey);
    }

    [Fact]
    public async Task An_unchanged_role_records_nothing_and_ends_no_session()
    {
        var target = Target(ClearanceLevel.Secret);
        var vault = new Vault(target);

        Assert.True(await vault.Service.SetRoleAsync(TargetId, ClearanceLevel.Secret, " global ", default));

        Assert.Empty(vault.Steps);
        Assert.Equal(0, target.CredentialGeneration);
    }

    [Fact]
    public async Task Disabling_commits_one_UserDisabled_event_with_the_generation_advanced_and_no_store_call()
    {
        var target = Target(ClearanceLevel.Standard);
        var vault = new Vault(target);

        Assert.True(await vault.Service.SetEnabledAsync(TargetId, enabled: false, default));

        Assert.Equal(["commit UserDisabled (generation 1)"], vault.Steps);
        Assert.True(target.IsDisabled);
    }

    [Fact]
    public async Task Enabling_commits_one_UserEnabled_event_with_the_generation_advanced_and_no_store_call()
    {
        // A disabled account may hold sessions that were ended only in the session store, which can
        // lose them, so enabling it ends them in the database as well.
        var target = Target(ClearanceLevel.Standard);
        target.IsDisabled = true;
        target.CredentialGeneration = 1;
        var vault = new Vault(target);

        Assert.True(await vault.Service.SetEnabledAsync(TargetId, enabled: true, default));

        Assert.Equal(["commit UserEnabled (generation 2)"], vault.Steps);
        Assert.False(target.IsDisabled);
    }

    [Fact]
    public async Task Enabling_an_enabled_account_records_nothing()
    {
        var vault = new Vault(Target(ClearanceLevel.Standard));

        Assert.True(await vault.Service.SetEnabledAsync(TargetId, enabled: true, default));

        Assert.Empty(vault.Steps);
    }

    [Fact]
    public async Task Unlocking_commits_one_AccountUnlocked_event_in_the_accounts_own_sign_in_history()
    {
        var target = Target(ClearanceLevel.Standard);
        target.FailedAccessCount = 4;
        target.LockedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(10);
        var vault = new Vault(target);

        Assert.True(await vault.Service.UnlockAsync(TargetId, default));

        Assert.Equal(["commit AccountUnlocked (generation 0)"], vault.Steps);
        var unlocked = Assert.Single(vault.Changes.Events);
        Assert.Equal(TargetId, unlocked.ActorUserId);
        Assert.False(unlocked.InheritActorUserId);
        Assert.Equal("vault-admin", unlocked.ActorUsername);
        Assert.Null(target.LockedUntilUtc);
        Assert.Equal(0, target.FailedAccessCount);
    }

    [Fact]
    public async Task Unlocking_an_unlocked_account_records_nothing()
    {
        var vault = new Vault(Target(ClearanceLevel.Standard));

        Assert.True(await vault.Service.UnlockAsync(TargetId, default));

        Assert.Empty(vault.Steps);
    }

    [Fact]
    public async Task Deleting_commits_the_removal_with_one_UserDeleted_event_and_no_store_call()
    {
        var vault = new Vault(Target(ClearanceLevel.Standard));

        Assert.True(await vault.Service.DeleteAsync(TargetId, default));

        Assert.Equal(["stage removal it-target", "commit UserDeleted (generation 0)"], vault.Steps);
        var deleted = Assert.Single(vault.Changes.Events);
        Assert.Equal(AdministratorId, deleted.ActorUserId);
        Assert.Equal(TargetId, deleted.ResourceId);
    }

    [Fact]
    public async Task Force_logout_revokes_every_session_before_it_records_the_event()
    {
        var vault = new Vault(Target(ClearanceLevel.Standard));

        Assert.True(await vault.Service.ForceLogoutAsync(TargetId, default));

        Assert.Equal(["revoke sessions", "audit UserForceLoggedOut"], vault.Steps);
    }

    [Fact]
    public async Task A_force_logout_whose_audit_fails_keeps_the_revocation_and_reports_it_unrecorded()
    {
        var vault = new Vault(Target(ClearanceLevel.Standard));
        vault.Audit.Fails = true;

        await Assert.ThrowsAsync<AuditWriteFailedException>(() => vault.Service.ForceLogoutAsync(TargetId, default));

        Assert.Equal(["revoke sessions"], vault.Steps);
    }

    private static CreateUserRequest NewPerson(string username)
        => new(username, "New", "Person", "a-long-enough-initial-password", ClearanceLevel.Standard, "global");

    private static User Target(ClearanceLevel clearance) => new()
    {
        Id = TargetId,
        Username = "it-target",
        DisplayName = "Target Account",
        Email = "target@example.test",
        Clearance = clearance,
        ProjectKey = "GLOBAL",
    };

    private static User Administrator() => new()
    {
        Id = AdministratorId,
        Username = "vault-admin",
        DisplayName = "Vault Administrator",
        Email = "admin@example.test",
        Clearance = ClearanceLevel.TopSecret,
        ProjectKey = "GLOBAL",
    };

    /// <summary>The service under test, wired to fakes that write every step to one shared list.</summary>
    private sealed class Vault
    {
        public Vault(params User[] accounts)
            : this(null, accounts)
        {
        }

        public Vault(INotificationService? notifications, params User[] accounts)
        {
            Users = new FakeUsers(Steps, [.. accounts]);
            Changes = new RecordingChanges(Steps, Users);
            Audit = new RecordingAudit(Steps);
            Service = new UserAdminService(
                Users,
                new StubHasher(),
                new RecordingRevocation(Steps),
                new UnusedRecoveryCodes(),
                new CleanBreachScreen(),
                new UserDirectoryOptions("example.test"),
                notifications ?? new RecordingNotifications(Steps),
                Audit,
                Changes,
                new SignedInAdministrator(),
                TimeProvider.System);
        }

        public List<string> Steps { get; } = [];

        public FakeUsers Users { get; }

        public RecordingChanges Changes { get; }

        public RecordingAudit Audit { get; }

        public UserAdminService Service { get; }
    }

    private sealed class FakeUsers(List<string> steps, List<User> accounts) : IUserRepository
    {
        public List<User> Accounts { get; } = accounts;

        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Accounts.FirstOrDefault(account => account.Id == id));
        public Task<User?> FindByUsernameAsync(string username, CancellationToken ct) => Task.FromResult(Accounts.FirstOrDefault(account => account.Username == username));
        public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<User>>(Accounts);
        public Task<IReadOnlyList<string>> FindEmailsWithPrefixAsync(string localPrefix, string domain, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<bool> IsEmailTakenAsync(string email, Guid accountId, CancellationToken ct) => Task.FromResult(false);

        public void StageAddition(User user) => steps.Add($"stage new {user.Username}");

        public void StageRemoval(User user) => steps.Add($"stage removal {user.Username}");

        public Task AddAsync(User user, CancellationToken ct)
        {
            steps.Add($"save new {user.Username} on its own");
            return Task.CompletedTask;
        }

        public Task UpdateAsync(User user, CancellationToken ct)
        {
            steps.Add($"save {user.Username} on its own");
            return Task.CompletedTask;
        }

        public Task<User?> FindByUsernameOrEmailAsync(string identifier, CancellationToken ct) => throw new NotSupportedException();
        public Task<User?> FindByEmailAsync(string email, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ReloadAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]?> GetAvatarPngAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task StageAvatarAsync(User user, byte[] png, CancellationToken ct) => throw new NotSupportedException();
        public Task StageAvatarRemovalAsync(User user, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryAcceptTotpStepAsync(User user, long step, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryCompleteTotpEnrollmentAsync(User user, long step, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> RecordFailedAttemptAsync(User user, LockoutPolicy lockout, DateTimeOffset nowUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearFailedAttemptsAsync(User user, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingChanges(List<string> steps, FakeUsers users) : IAccountChangeCommitter
    {
        public Exception? Failure { get; set; }

        /// <summary>An account another administrator saves just before this commit fails, as when a race is lost.</summary>
        public User? SavedFirstByAnother { get; set; }

        public List<AuditEntry> Events { get; } = [];

        public Task<bool> CommitAsync(User account, AuditEntry entry, CancellationToken ct)
        {
            if (SavedFirstByAnother is not null)
            {
                users.Accounts.Add(SavedFirstByAnother);
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            steps.Add($"commit {entry.Action} (generation {account.CredentialGeneration})");
            Events.Add(entry);
            return Task.FromResult(true);
        }

        public Task<bool> CommitAsync(User account, AuditAction action, string details, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CommitAsync(User account, AuditAction action, string details, Func<CancellationToken, Task> recheck, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingAudit(List<string> steps) : IAuditSink
    {
        public bool Fails { get; set; }

        public Task WriteAsync(AuditEntry entry, CancellationToken ct)
        {
            if (Fails)
            {
                throw new AuditWriteFailedException("rejected");
            }

            steps.Add($"audit {entry.Action}");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRevocation(List<string> steps) : ISessionRevocationService
    {
        public Task RevokeAsync(Guid userId, CancellationToken ct = default)
        {
            steps.Add("revoke sessions");
            return Task.CompletedTask;
        }

        public Task<string> CaptureAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> FindAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>A mail transport that is down and an outbox that cannot be written.</summary>
    private sealed class BrokenMail : IEmailSender, IEmailLogRepository
    {
        public string Transport => "Smtp";

        public Task SendAsync(EmailMessage message, CancellationToken ct) => throw new InvalidOperationException("The mail server is down.");
        public Task AddAsync(EmailLog entry, CancellationToken ct) => throw new InvalidOperationException("The outbox is unavailable.");
        public Task<IReadOnlyList<EmailLog>> ListRecentAsync(int max, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingNotifications(List<string> steps) : INotificationService
    {
        public Task NotifyUserProvisionedAsync(string email, string displayName, string username, CancellationToken ct)
        {
            steps.Add($"email {email}");
            return Task.CompletedTask;
        }

        public Task NotifyAccessRequestDecidedAsync(Guid requesterUserId, string secretName, bool approved, string reviewer, string? note, CancellationToken ct) => throw new NotSupportedException();
        public Task NotifySensitiveChangeAsync(string trustedAddress, ProfileOperationKind kind, CancellationToken ct) => throw new NotSupportedException();
        public Task NotifyPasswordChangedAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task NotifyCredentialChangedAsync(Guid userId, ProfileOperationKind kind, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> NotifyExpiringSecretAsync(Guid ownerUserId, string secretName, DateTimeOffset expiresAtUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<EmailLogDto>> ListRecentAsync(int max, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubHasher : IPasswordHasher
    {
        public PasswordHashResult Hash(string password) => new([1, 2, 3], [4, 5, 6]);
        public bool Verify(string password, byte[] hash, byte[] salt) => throw new NotSupportedException();
    }

    private sealed class CleanBreachScreen : IBreachedPasswordScreen
    {
        public bool IsCompromised(string password) => false;
        public int CorpusSize => 0;
    }

    private sealed class UnusedRecoveryCodes : IMfaRecoveryCodeRepository
    {
        public Task<IReadOnlyList<MfaRecoveryCode>> ListUnusedAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CountUnusedAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task StageReplacementAsync(Guid userId, IReadOnlyList<MfaRecoveryCode> codes, CancellationToken ct) => throw new NotSupportedException();
        public Task StageRemovalOfAllAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryRedeemAsync(MfaRecoveryCode code, DateTimeOffset usedAtUtc, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class SignedInAdministrator : IAuditContext
    {
        public Guid? UserId => AdministratorId;
        public string? Username => "vault-admin";
        public string? SourceIp => "::1";
    }
}
