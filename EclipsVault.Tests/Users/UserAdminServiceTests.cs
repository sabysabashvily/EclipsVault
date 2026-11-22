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
    public async Task Creating_an_account_commits_it_invited_with_its_invitation_and_one_UserCreated_event_then_mails_the_entered_address()
    {
        var vault = new Vault();

        var created = await vault.Service.CreateAsync(NewPerson("it-new-person"), default);

        Assert.Equal(["stage new it-new-person", $"stage invitation {created.Id}", "commit UserCreated (generation 0)",
            "invitation-mail new.person@example.test"], vault.Steps);
        Assert.Equal("new.person@example.test", created.Email);
        Assert.True(created.Invitation.Delivered);
        var invitation = Assert.Single(vault.Invitations.Rows);
        Assert.True(InvitationToken.Matches(Assert.Single(vault.InvitationDelivery.Sent).Token, invitation.TokenHash));
        Assert.Equal(invitation.CreatedAtUtc + InvitationToken.Lifetime, created.Invitation.ExpiresAtUtc);
    }

    [Fact]
    public async Task A_create_is_refused_before_anything_is_staged_when_invitations_are_unavailable_or_the_address_is_taken()
    {
        var unavailable = new Vault();
        unavailable.InvitationDelivery.Available = false;
        await Assert.ThrowsAsync<InvitationsUnavailableException>(() => unavailable.Service.CreateAsync(NewPerson("it-new-person"), default));
        Assert.Empty(unavailable.Steps);

        var holder = Target(ClearanceLevel.Standard);
        holder.Email = "New.Person@example.test";
        var taken = new Vault(holder);
        await Assert.ThrowsAsync<EmailAddressTakenException>(() => taken.Service.CreateAsync(NewPerson("it-new-person"), default));
        Assert.Empty(taken.Steps);
    }

    [Fact]
    public async Task Resending_an_invitation_commits_a_new_token_hash_and_InvitationSent_before_delivering_to_the_stored_address()
    {
        var invited = Target(ClearanceLevel.Standard);
        invited.AwaitingActivation = true;
        invited.Email = "person@example.test";
        var vault = new Vault(invited, Administrator());
        var previousHash = InvitationToken.Hash("old-token");
        vault.Invitations.Rows.Add(new AccountInvitation
        {
            Id = Guid.NewGuid(),
            UserId = invited.Id,
            TokenHash = previousHash,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
            PendingTotpSecret = "OLDSEED"
        });

        var sent = await vault.Service.ResendInvitationAsync(invited.Id, default);

        Assert.NotNull(sent);
        Assert.True(sent.Delivered);
        Assert.Equal(["commit InvitationSent (generation 0)", "invitation-mail person@example.test"], vault.Steps);
        var stored = Assert.Single(vault.Invitations.Rows);
        Assert.Null(stored.SpentAtUtc);
        Assert.Null(stored.PendingTotpSecret);
        Assert.False(previousHash.SequenceEqual(stored.TokenHash));
        var mail = Assert.Single(vault.InvitationDelivery.Sent);
        Assert.Equal("person@example.test", mail.Address);
        Assert.True(InvitationToken.Matches(mail.Token, stored.TokenHash));
        Assert.DoesNotContain(mail.Token, vault.Changes.Events.Single().Details!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resending_refuses_an_active_account_or_unavailable_delivery_without_saving_or_mailing()
    {
        var active = Target(ClearanceLevel.Standard);
        var activeVault = new Vault(active, Administrator());

        await Assert.ThrowsAsync<AccountAlreadyActiveException>(() => activeVault.Service.ResendInvitationAsync(active.Id, default));
        Assert.Empty(activeVault.Steps);
        Assert.Empty(activeVault.InvitationDelivery.Sent);

        var invited = Target(ClearanceLevel.Standard);
        invited.AwaitingActivation = true;
        var unavailableVault = new Vault(invited, Administrator());
        unavailableVault.InvitationDelivery.Available = false;

        await Assert.ThrowsAsync<InvitationsUnavailableException>(() => unavailableVault.Service.ResendInvitationAsync(invited.Id, default));
        Assert.Empty(unavailableVault.Steps);
        Assert.Empty(unavailableVault.InvitationDelivery.Sent);
    }

    [Fact]
    public async Task Replacing_an_invitation_address_spends_the_live_token_audits_the_new_address_and_sends_no_mail()
    {
        var invited = Target(ClearanceLevel.Standard);
        invited.AwaitingActivation = true;
        invited.Email = "wrong@example.test";
        var vault = new Vault(invited, Administrator());
        var live = new AccountInvitation
        {
            Id = Guid.NewGuid(),
            UserId = invited.Id,
            TokenHash = InvitationToken.Hash("live-token"),
            CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(71),
            PendingTotpSecret = "PENDINGSEED"
        };
        vault.Invitations.Rows.Add(live);

        var replaced = await vault.Service.ReplaceInvitationAddressAsync(invited.Id, "  right@example.test  ", default);

        Assert.Equal("right@example.test", replaced);
        Assert.Equal("right@example.test", invited.Email);
        Assert.NotNull(live.SpentAtUtc);
        Assert.Null(live.PendingTotpSecret);
        Assert.Equal(["commit InvitationAddressReplaced (generation 0)"], vault.Steps);
        Assert.Contains("right@example.test", vault.Changes.Events.Single().Details!, StringComparison.Ordinal);
        Assert.Empty(vault.InvitationDelivery.Sent);
    }

    [Fact]
    public async Task Listing_shows_an_invitations_expiry_only_while_it_can_still_activate_the_account()
    {
        var now = DateTimeOffset.UtcNow;
        var usable = Invited("it-list-usable");
        var expired = Invited("it-list-expired");
        var spent = Invited("it-list-spent");
        var vault = new Vault(usable, expired, spent, Administrator());
        vault.Invitations.Rows.AddRange(
        [
            Invitation(usable, now.AddDays(2), spentAtUtc: null),
            Invitation(expired, now.AddDays(-1), spentAtUtc: null),
            Invitation(spent, now.AddDays(2), spentAtUtc: now),
        ]);

        var listed = (await vault.Service.ListAsync(default)).ToDictionary(account => account.Username);

        Assert.Equal(vault.Invitations.Rows[0].ExpiresAtUtc, listed["it-list-usable"].InvitationExpiresAtUtc);
        Assert.Null(listed["it-list-expired"].InvitationExpiresAtUtc);
        Assert.Null(listed["it-list-spent"].InvitationExpiresAtUtc);
        Assert.All([listed["it-list-usable"], listed["it-list-expired"], listed["it-list-spent"]],
            account => Assert.True(account.AwaitingActivation));
        Assert.False(listed["vault-admin"].AwaitingActivation);
        Assert.Equal(vault.Invitations.Rows[0].ExpiresAtUtc, (await vault.Service.GetAsync(usable.Id, default))!.InvitationExpiresAtUtc);
        Assert.Null((await vault.Service.GetAsync(spent.Id, default))!.InvitationExpiresAtUtc);

        static User Invited(string username) => new()
        {
            Id = Guid.NewGuid(),
            Username = username,
            DisplayName = "Invited Person",
            Email = username + "@example.test",
            ProjectKey = "GLOBAL",
            AwaitingActivation = true,
        };

        static AccountInvitation Invitation(User account, DateTimeOffset expiresAtUtc, DateTimeOffset? spentAtUtc) => new()
        {
            Id = Guid.NewGuid(),
            UserId = account.Id,
            TokenHash = InvitationToken.Hash(account.Username),
            ExpiresAtUtc = expiresAtUtc,
            SpentAtUtc = spentAtUtc,
        };
    }

    [Fact]
    public async Task Resetting_an_invited_accounts_authenticator_is_refused_as_not_activated()
    {
        var invited = Target(ClearanceLevel.Standard);
        invited.AwaitingActivation = true;
        var vault = new Vault(invited, Administrator());

        await Assert.ThrowsAsync<AccountNotActivatedException>(() => vault.Service.ResetTotpAsync(invited.Id, default));
        Assert.Empty(vault.Steps);
    }

    [Fact]
    public async Task An_invited_top_secret_account_does_not_count_as_an_enabled_administrator()
    {
        static (User ActiveAdmin, User InvitedAdmin, Vault Vault) Setup()
        {
            var activeAdmin = Target(ClearanceLevel.TopSecret);
            var invitedAdmin = new User
            {
                Id = Guid.NewGuid(),
                Username = "invited-admin",
                Email = "invited.admin@example.test",
                Clearance = ClearanceLevel.TopSecret,
                ProjectKey = "GLOBAL",
                AwaitingActivation = true
            };
            return (activeAdmin, invitedAdmin, new Vault(activeAdmin, invitedAdmin));
        }

        var demote = Setup();
        await Assert.ThrowsAsync<LastAdministratorException>(() => demote.Vault.Service.SetRoleAsync(demote.ActiveAdmin.Id, ClearanceLevel.Secret, "GLOBAL", default));

        var disable = Setup();
        await Assert.ThrowsAsync<LastAdministratorException>(() => disable.Vault.Service.SetEnabledAsync(disable.ActiveAdmin.Id, false, default));

        var delete = Setup();
        await Assert.ThrowsAsync<LastAdministratorException>(() => delete.Vault.Service.DeleteAsync(delete.ActiveAdmin.Id, default));

        var demoteInvited = Setup();
        Assert.True(await demoteInvited.Vault.Service.SetRoleAsync(demoteInvited.InvitedAdmin.Id, ClearanceLevel.Standard, "GLOBAL", default));
    }

    [Fact]
    public async Task A_create_whose_commit_fails_mails_no_one_and_reports_the_audit_trail_unavailable()
    {
        var vault = new Vault();
        vault.Changes.Failures.Enqueue(new AuditWriteFailedException("rejected"));

        await Assert.ThrowsAsync<AuditWriteFailedException>(() => vault.Service.CreateAsync(NewPerson("it-unrecorded"), default));

        Assert.Empty(vault.Changes.Events);
        Assert.Empty(vault.InvitationDelivery.Sent);
    }

    [Fact]
    public async Task A_create_whose_invitation_is_not_delivered_still_creates_the_account_and_says_so()
    {
        var vault = new Vault();
        vault.InvitationDelivery.Delivered = false;

        var created = await vault.Service.CreateAsync(NewPerson("it-unmailed"), default);

        Assert.False(created.Invitation.Delivered);
        Assert.Equal(AuditAction.UserCreated, Assert.Single(vault.Changes.Events).Action);
    }

    [Fact]
    public async Task A_create_that_loses_the_username_race_answers_the_checks_conflict_and_emails_no_one()
    {
        var vault = new Vault();
        vault.Changes.Failures.Enqueue(new UsernameTakenException());

        var conflict = await Assert.ThrowsAsync<UsernameTakenException>(() => vault.Service.CreateAsync(NewPerson("it-raced"), default));

        Assert.Equal("The username 'it-raced' is already taken.", conflict.Message);
        Assert.Empty(vault.InvitationDelivery.Sent);
    }

    [Fact]
    public async Task A_create_that_loses_its_address_to_the_same_username_answers_the_username_conflict()
    {
        var vault = new Vault();
        vault.Changes.SavedFirstByAnother = new User { Id = Guid.NewGuid(), Username = "it-raced", Email = "new.person@example.test" };
        vault.Changes.Failures.Enqueue(new EmailAddressTakenException());

        var conflict = await Assert.ThrowsAsync<UsernameTakenException>(() => vault.Service.CreateAsync(NewPerson("it-raced"), default));

        Assert.Equal("The username 'it-raced' is already taken.", conflict.Message);
    }

    [Fact]
    public async Task A_create_that_loses_its_address_to_another_account_answers_the_generic_address_conflict()
    {
        var vault = new Vault();
        vault.Changes.SavedFirstByAnother = new User { Id = Guid.NewGuid(), Username = "it-other-person", Email = "new.person@example.test" };
        vault.Changes.Failures.Enqueue(new EmailAddressTakenException());

        await Assert.ThrowsAsync<EmailAddressTakenException>(() => vault.Service.CreateAsync(NewPerson("it-raced"), default));

        Assert.Empty(vault.InvitationDelivery.Sent);
    }

    [Fact]
    public async Task Changing_a_role_commits_one_UserRoleChanged_event_with_the_generation_advanced_and_no_store_call()
    {
        var target = Target(ClearanceLevel.TopSecret);
        var vault = new Vault(target, Administrator());

        Assert.True(await vault.Service.SetRoleAsync(TargetId, ClearanceLevel.Standard, " payments ", default));

        Assert.Equal(["lock administrators", "commit UserRoleChanged (generation 1)"], vault.Steps);
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

    private const string LastToDemote = "Cannot demote the last TopSecret administrator — the vault would become unmanageable.";
    private const string LastToDisable = "Cannot disable the last TopSecret administrator.";
    private const string LastToDelete = "Cannot delete the last TopSecret administrator — the vault would become unmanageable.";

    private static readonly string[] Demoted = ["lock administrators", "commit UserRoleChanged (generation 1)"];
    private static readonly string[] Disabled = ["lock administrators", "commit UserDisabled (generation 1)"];
    private static readonly string[] Deleted = ["stage removal it-target", "lock administrators", "commit UserDeleted (generation 0)"];

    /// <summary>
    /// The enabled administrator it-target is demoted, disabled or deleted while the vault holds one other
    /// account. The administrator signed in is not an account here, so only that other account decides
    /// whether an enabled administrator would remain.
    /// </summary>
    public static TheoryData<Guid, Func<UserAdminService, Task>, User[], string[]> LastAdministratorCases => new()
    {
        // signed in as, change to it-target, the other account, what reached the vault
        { AdministratorId, Demote, [OtherAdministrator(enabled: true)], Demoted },
        { AdministratorId, Demote, [OtherAdministrator(enabled: false)], ["lock administrators", $"refused: {LastToDemote}"] },
        { AdministratorId, Demote, [], ["lock administrators", $"refused: {LastToDemote}"] },
        { TargetId, Demote, [OtherAdministrator(enabled: true)], Demoted },
        { TargetId, Demote, [OtherAdministrator(enabled: false)], ["lock administrators", $"refused: {LastToDemote}"] },
        { TargetId, Demote, [], ["lock administrators", $"refused: {LastToDemote}"] },
        { AdministratorId, Disable, [OtherAdministrator(enabled: true)], Disabled },
        { AdministratorId, Disable, [OtherAdministrator(enabled: false)], ["lock administrators", $"refused: {LastToDisable}"] },
        { AdministratorId, Disable, [], ["lock administrators", $"refused: {LastToDisable}"] },
        { AdministratorId, Delete, [OtherAdministrator(enabled: true)], Deleted },
        { AdministratorId, Delete, [OtherAdministrator(enabled: false)], ["stage removal it-target", "lock administrators", $"refused: {LastToDelete}"] },
        { AdministratorId, Delete, [], ["stage removal it-target", "lock administrators", $"refused: {LastToDelete}"] },
    };

    [Theory]
    [MemberData(nameof(LastAdministratorCases))]
    public async Task Removing_an_enabled_administrator_is_refused_under_the_lock_unless_another_enabled_one_remains(
        Guid signedInAs, Func<UserAdminService, Task> change, User[] otherAccounts, string[] expected)
    {
        var vault = new Vault(signedInAs, [Target(ClearanceLevel.TopSecret), .. otherAccounts]);

        await Record.ExceptionAsync(() => change(vault.Service));

        Assert.Equal(expected, vault.Steps);
    }

    [Fact]
    public async Task Demoting_a_disabled_administrator_takes_no_lock_and_needs_no_other_administrator()
    {
        var target = Target(ClearanceLevel.TopSecret);
        target.IsDisabled = true;
        var vault = new Vault(target);

        Assert.True(await vault.Service.SetRoleAsync(TargetId, ClearanceLevel.Standard, "GLOBAL", default));

        Assert.Equal(["commit UserRoleChanged (generation 1)"], vault.Steps);
    }

    [Fact]
    public async Task Deleting_a_disabled_administrator_takes_no_lock_and_needs_no_other_administrator()
    {
        var target = Target(ClearanceLevel.TopSecret);
        target.IsDisabled = true;
        var vault = new Vault(target);

        Assert.True(await vault.Service.DeleteAsync(TargetId, default));

        Assert.Equal(["stage removal it-target", "commit UserDeleted (generation 0)"], vault.Steps);
    }

    public static TheoryData<Func<UserAdminService, Task>, string> OwnAccountChanges => new()
    {
        { service => service.ResetTotpAsync(AdministratorId, default), "You cannot reset your own MFA here. Reset it from Profile, which first confirms it is you." },
        { service => service.SetEnabledAsync(AdministratorId, enabled: false, default), "You cannot disable your own account while signed in with it." },
        { service => service.DeleteAsync(AdministratorId, default), "You cannot delete your own account while signed in with it." },
    };

    [Theory]
    [MemberData(nameof(OwnAccountChanges))]
    public async Task An_administrator_cannot_reset_disable_or_delete_the_account_they_are_signed_in_with(
        Func<UserAdminService, Task> change, string refusal)
    {
        var vault = new Vault(Administrator(), OtherAdministrator(enabled: true));

        var refused = await Assert.ThrowsAsync<OwnAccountException>(() => change(vault.Service));

        Assert.Equal(refusal, refused.Message);
        Assert.Empty(vault.Steps);
    }

    [Fact]
    public async Task An_administrator_can_still_force_their_own_sessions_to_end()
    {
        var vault = new Vault(Administrator());

        Assert.True(await vault.Service.ForceLogoutAsync(AdministratorId, default));

        Assert.Equal(["revoke sessions", "audit UserForceLoggedOut"], vault.Steps);
    }

    private const string UsernameRule = "Usernames are 3 to 64 characters. Use letters, digits, underscores, hyphens or dots.";
    private const string ClearanceRule = "Choose one of the vault's clearance levels.";
    private const string ProjectRule = "Project keys are 1 to 64 characters, not counting spaces at either end.";

    public static TheoryData<CreateUserRequest, string, string> RefusedNewAccounts => new()
    {
        // request, the field named, the message
        { NewPerson("ab"), "Username", UsernameRule },
        { NewPerson("it new person"), "Username", UsernameRule },
        { NewPerson("it-new-person") with { FirstName = "   " }, "FirstName", "First name is required." },
        { NewPerson("it-new-person") with { FirstName = new string('f', 65) }, "FirstName", "First name can be at most 64 characters." },
        { NewPerson("it-new-person") with { LastName = "" }, "LastName", "Last name is required." },
        { NewPerson("it-new-person") with { LastName = new string('l', 65) }, "LastName", "Last name can be at most 64 characters." },
        { NewPerson("it-new-person") with { FirstName = new string('f', 32), LastName = new string('l', 32) }, "LastName", "First and last name together can be at most 64 characters, counting the space between them." },
        { NewPerson("it-new-person") with { Email = "not-an-address" }, "Email", "Enter a valid email address." },
        { NewPerson("it-new-person") with { Email = "  " }, "Email", "Enter a valid email address." },
        { NewPerson("it-new-person") with { Clearance = (ClearanceLevel)7 }, "Clearance", ClearanceRule },
        { NewPerson("it-new-person") with { Clearance = 0 }, "Clearance", ClearanceRule },
        { NewPerson("it-new-person") with { ProjectKey = "   " }, "ProjectKey", ProjectRule },
        { NewPerson("it-new-person") with { ProjectKey = new string('p', 65) }, "ProjectKey", ProjectRule },
    };

    [Theory]
    [MemberData(nameof(RefusedNewAccounts))]
    public async Task A_new_account_that_breaks_an_input_rule_is_refused_naming_its_field(CreateUserRequest request, string field, string message)
    {
        var vault = new Vault();

        var refused = await Assert.ThrowsAsync<AccountInputException>(() => vault.Service.CreateAsync(request, default));

        Assert.Equal((field, message), (refused.Field, refused.Message));
        Assert.Empty(vault.Steps);
    }

    public static TheoryData<CreateUserRequest, string> NewAccountsAtTheLimits => new()
    {
        // request, the display name it gets
        { NewPerson("it-new-person") with { FirstName = new string('f', 31), LastName = new string('l', 32) }, "fffffffffffffffffffffffffffffff llllllllllllllllllllllllllllllll" },
        { NewPerson("it-new-person") with { FirstName = "  Ann ", LastName = " Lee  " }, "Ann Lee" },
        { NewPerson("it-new-person") with { ProjectKey = new string('p', 64) }, "New Person" },
        { NewPerson("it-new-person") with { ProjectKey = " x " }, "New Person" },
    };

    [Theory]
    [MemberData(nameof(NewAccountsAtTheLimits))]
    public async Task A_new_account_at_the_limits_of_the_input_rules_is_created(CreateUserRequest request, string displayName)
    {
        var vault = new Vault();

        var created = await vault.Service.CreateAsync(request, default);

        Assert.Equal(displayName, created.DisplayName);
    }

    public static TheoryData<ClearanceLevel, string, string, string> RefusedRoles => new()
    {
        // clearance, project, the field named, the message
        { (ClearanceLevel)7, "GLOBAL", "Clearance", ClearanceRule },
        { 0, "GLOBAL", "Clearance", ClearanceRule },
        { ClearanceLevel.Secret, " ", "ProjectKey", ProjectRule },
        { ClearanceLevel.Secret, new string('p', 65), "ProjectKey", ProjectRule },
    };

    [Theory]
    [MemberData(nameof(RefusedRoles))]
    public async Task A_role_that_breaks_an_input_rule_is_refused_naming_its_field(ClearanceLevel clearance, string project, string field, string message)
    {
        var target = Target(ClearanceLevel.Standard);
        var vault = new Vault(target);

        var refused = await Assert.ThrowsAsync<AccountInputException>(() => vault.Service.SetRoleAsync(TargetId, clearance, project, default));

        Assert.Equal((field, message), (refused.Field, refused.Message));
        Assert.Empty(vault.Steps);
        Assert.Equal(ClearanceLevel.Standard, target.Clearance);
    }

    private static Task Demote(UserAdminService service) => service.SetRoleAsync(TargetId, ClearanceLevel.Secret, "GLOBAL", default);

    private static Task Disable(UserAdminService service) => service.SetEnabledAsync(TargetId, enabled: false, default);

    private static Task Delete(UserAdminService service) => service.DeleteAsync(TargetId, default);

    private static User OtherAdministrator(bool enabled) => new()
    {
        Id = Guid.Parse("0a000000-0000-0000-0000-000000000002"),
        Username = "it-other-administrator",
        DisplayName = "Other Administrator",
        Email = "other@example.test",
        Clearance = ClearanceLevel.TopSecret,
        ProjectKey = "GLOBAL",
        IsDisabled = !enabled,
    };

    private static CreateUserRequest NewPerson(string username)
        => new(username, "New", "Person", "new.person@example.test", ClearanceLevel.Standard, "global");

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
            : this(AdministratorId, accounts)
        {
        }

        public Vault(Guid signedInAs, params User[] accounts)
        {
            Users = new FakeUsers(Steps, [.. accounts]);
            Invitations = new FakeInvitations(Steps);
            InvitationDelivery = new RecordingInvitationDelivery(Steps);
            Changes = new RecordingChanges(Steps, Users);
            Audit = new RecordingAudit(Steps);
            Service = new UserAdminService(
                Users,
                Invitations,
                InvitationDelivery,
                new RecordingRevocation(Steps),
                new UnusedRecoveryCodes(),
                Audit,
                Changes,
                new SignedInAdministrator(signedInAs),
                TimeProvider.System);
        }

        public List<string> Steps { get; } = [];

        public FakeUsers Users { get; }

        public FakeInvitations Invitations { get; }

        public RecordingInvitationDelivery InvitationDelivery { get; }

        public RecordingChanges Changes { get; }

        public RecordingAudit Audit { get; }

        public UserAdminService Service { get; }
    }

    private sealed class FakeInvitations(List<string> steps) : IAccountInvitationRepository
    {
        public List<AccountInvitation> Rows { get; } = [];

        public Task<AccountInvitation?> FindByUserIdAsync(Guid userId, CancellationToken ct)
            => Task.FromResult(Rows.FirstOrDefault(i => i.UserId == userId));

        public Task<AccountInvitation?> FindByTokenHashAsync(byte[] tokenHash, CancellationToken ct)
            => Task.FromResult(Rows.FirstOrDefault(i => i.TokenHash.SequenceEqual(tokenHash)));

        public Task<IReadOnlyList<AccountInvitation>> ListAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AccountInvitation>>(Rows);

        public Task<string?> StorePendingSecretOnceAsync(Guid invitationId, byte[] tokenHash, string seed, DateTimeOffset nowUtc, CancellationToken ct)
            => throw new NotSupportedException();

        public void StageAddition(AccountInvitation invitation)
        {
            Rows.Add(invitation);
            steps.Add($"stage invitation {invitation.UserId}");
        }
    }

    private sealed class RecordingInvitationDelivery(List<string> steps) : IAccountInvitationDelivery
    {
        public bool Available { get; set; } = true;
        public bool Delivered { get; set; } = true;
        public List<(string Address, string DisplayName, string Username, string Token)> Sent { get; } = [];

        public Task<bool> TrySendAsync(string address, string displayName, string username, string token, CancellationToken ct)
        {
            Sent.Add((address, displayName, username, token));
            steps.Add($"invitation-mail {address}");
            return Task.FromResult(Delivered);
        }
    }

    private sealed class FakeUsers(List<string> steps, List<User> accounts) : IUserRepository
    {
        public List<User> Accounts { get; } = accounts;

        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Accounts.FirstOrDefault(account => account.Id == id));
        public Task<User?> FindByUsernameAsync(string username, CancellationToken ct) => Task.FromResult(Accounts.FirstOrDefault(account => account.Username == username));
        public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<User>>(Accounts);

        public Task<bool> AnyOtherEnabledAdministratorAsync(Guid accountId, CancellationToken ct)
            => Task.FromResult(Accounts.Any(account => account.Id != accountId
                && account.Clearance == ClearanceLevel.TopSecret
                && !account.IsDisabled
                && !account.AwaitingActivation));
        public Task<bool> IsEmailTakenAsync(string email, Guid accountId, CancellationToken ct)
            => Task.FromResult(Accounts.Any(account => account.Id != accountId
                && account.NormalizedEmail == EclipsVault.Core.Domain.EmailIdentity.Normalize(email)));

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
        /// <summary>The failures the next commits meet, one each, as the database reports them.</summary>
        public Queue<Exception> Failures { get; } = new();

        /// <summary>An account another administrator saves just before the next commit, as when a race is lost.</summary>
        public User? SavedFirstByAnother { get; set; }

        public List<AuditEntry> Events { get; } = [];

        public Task<bool> CommitAsync(User account, AuditEntry entry, CancellationToken ct)
        {
            if (SavedFirstByAnother is not null)
            {
                users.Accounts.Add(SavedFirstByAnother);
                SavedFirstByAnother = null;
            }

            if (Failures.TryDequeue(out var failure))
            {
                throw failure;
            }

            steps.Add($"commit {entry.Action} (generation {account.CredentialGeneration})");
            Events.Add(entry);
            return Task.FromResult(true);
        }

        public async Task<bool> CommitAsync(User account, AuditEntry entry, Func<CancellationToken, Task> recheck, CancellationToken ct)
        {
            await recheck(ct);
            return await CommitAsync(account, entry, ct);
        }

        public async Task<bool> CommitAdministratorChangeAsync(User account, AuditEntry entry, Func<CancellationToken, Task> recheck, CancellationToken ct)
        {
            steps.Add("lock administrators");
            try
            {
                await recheck(ct);
            }
            catch (LastAdministratorException refusal)
            {
                steps.Add($"refused: {refusal.Message}");
                throw;
            }

            return await CommitAsync(account, entry, ct);
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

    private sealed class UnusedRecoveryCodes : IMfaRecoveryCodeRepository
    {
        public Task<IReadOnlyList<MfaRecoveryCode>> ListUnusedAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CountUnusedAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task StageReplacementAsync(Guid userId, IReadOnlyList<MfaRecoveryCode> codes, CancellationToken ct) => throw new NotSupportedException();
        public Task StageRemovalOfAllAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryRedeemAsync(MfaRecoveryCode code, DateTimeOffset usedAtUtc, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class SignedInAdministrator(Guid accountId) : IAuditContext
    {
        public Guid? UserId => accountId;
        public string? Username => "vault-admin";
        public string? SourceIp => "::1";
    }
}
