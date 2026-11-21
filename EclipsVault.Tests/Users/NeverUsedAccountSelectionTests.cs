using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.Tests.Users;

public class NeverUsedAccountSelectionTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Only_accounts_with_no_totp_no_passkey_and_no_recorded_sign_in_are_invited()
    {
        var activeAdmin = Account("vault-admin", ClearanceLevel.TopSecret, totpEnabled: true);
        var neverUsed = Account("never-used", ClearanceLevel.Standard);
        var totpUser = Account("has-totp", ClearanceLevel.Standard, totpEnabled: true);
        var passkeyUser = Account("has-passkey", ClearanceLevel.Standard, hasPasskey: true);
        var signedInThenReset = Account("reset-after-sign-in", ClearanceLevel.Standard, hasRecordedSignIn: true);

        var invited = NeverUsedAccountSelection.SelectAccountsToInvite(
            [activeAdmin, neverUsed, totpUser, passkeyUser, signedInThenReset]);

        Assert.Equal([neverUsed.Id], invited);
    }

    [Fact]
    public void When_every_enabled_administrator_would_be_invited_vault_admin_stays()
    {
        var earlierAdmin = Account("early-admin", ClearanceLevel.TopSecret, createdAtUtc: BaseTime);
        var bootstrapAdmin = Account("vault-admin", ClearanceLevel.TopSecret, createdAtUtc: BaseTime.AddDays(1));
        var standardUser = Account("standard", ClearanceLevel.Standard, createdAtUtc: BaseTime.AddDays(2));

        var invited = NeverUsedAccountSelection.SelectAccountsToInvite(
            [earlierAdmin, bootstrapAdmin, standardUser]);

        Assert.DoesNotContain(bootstrapAdmin.Id, invited);
        Assert.Contains(earlierAdmin.Id, invited);
        Assert.Contains(standardUser.Id, invited);
    }

    [Fact]
    public void When_every_enabled_administrator_would_be_invited_and_vault_admin_is_absent_the_earliest_created_stays()
    {
        var laterAdmin = Account("later-admin", ClearanceLevel.TopSecret, createdAtUtc: BaseTime.AddHours(2));
        var earliestAdmin = Account("earliest-admin", ClearanceLevel.TopSecret, createdAtUtc: BaseTime);
        var disabledEarlierAdmin = Account("disabled-admin", ClearanceLevel.TopSecret, isDisabled: true, createdAtUtc: BaseTime.AddHours(-1));

        var invited = NeverUsedAccountSelection.SelectAccountsToInvite(
            [laterAdmin, earliestAdmin, disabledEarlierAdmin]);

        Assert.DoesNotContain(earliestAdmin.Id, invited);
        Assert.Contains(laterAdmin.Id, invited);
        Assert.Contains(disabledEarlierAdmin.Id, invited);
    }

    [Fact]
    public void A_disabled_vault_admin_is_not_preserved_when_another_enabled_administrator_exists()
    {
        var disabledVaultAdmin = Account("vault-admin", ClearanceLevel.TopSecret, isDisabled: true, createdAtUtc: BaseTime);
        var enabledAdmin = Account("enabled-admin", ClearanceLevel.TopSecret, createdAtUtc: BaseTime.AddDays(1));

        var invited = NeverUsedAccountSelection.SelectAccountsToInvite([disabledVaultAdmin, enabledAdmin]);

        Assert.Contains(disabledVaultAdmin.Id, invited);
        Assert.DoesNotContain(enabledAdmin.Id, invited);
    }

    [Fact]
    public void When_another_enabled_administrator_is_already_active_even_vault_admin_is_invited_if_never_used()
    {
        var activeAdmin = Account("active-admin", ClearanceLevel.TopSecret, hasRecordedSignIn: true);
        var unusedVaultAdmin = Account("vault-admin", ClearanceLevel.TopSecret);

        var invited = NeverUsedAccountSelection.SelectAccountsToInvite([activeAdmin, unusedVaultAdmin]);

        Assert.Equal([unusedVaultAdmin.Id], invited);
    }

    [Fact]
    public void Ties_on_creation_time_preserve_the_lowest_account_id_deterministically()
    {
        var lowerId = new Guid("00000000-0000-0000-0000-000000000001");
        var higherId = new Guid("00000000-0000-0000-0000-000000000002");
        var first = new NeverUsedAccountCandidate(higherId, "admin-b", ClearanceLevel.TopSecret, false, false, false, false, BaseTime);
        var second = new NeverUsedAccountCandidate(lowerId, "admin-a", ClearanceLevel.TopSecret, false, false, false, false, BaseTime);

        var invited = NeverUsedAccountSelection.SelectAccountsToInvite([first, second]);

        Assert.Equal([higherId], invited);
    }

    private static NeverUsedAccountCandidate Account(
        string username,
        ClearanceLevel clearance,
        bool isDisabled = false,
        bool totpEnabled = false,
        bool hasPasskey = false,
        bool hasRecordedSignIn = false,
        DateTimeOffset? createdAtUtc = null)
        => new(Guid.NewGuid(), username, clearance, isDisabled, totpEnabled, hasPasskey, hasRecordedSignIn, createdAtUtc ?? BaseTime);
}
