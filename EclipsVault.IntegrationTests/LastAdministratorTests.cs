using System.Net;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The vault keeps an enabled administrator. A disabled administrator does not count, so the only
/// enabled one cannot demote themselves, and the refusal changes nothing.
/// </summary>
[Collection(SingleAdministratorCollection.Name)]
public sealed class LastAdministratorTests(SingleAdministratorFixture fixture)
{
    [Fact]
    public async Task The_only_enabled_administrator_cannot_demote_themselves_beside_a_disabled_one()
    {
        var vault = fixture.Vault;
        var admin = await vault.AdminClientAsync();
        await LegacyUsers.CreateAsync(admin, "it-last-disabled-admin");
        var disabled = await ProfileHttp.AccountAsync(vault, "it-last-disabled-admin");
        await LegacyUsers.EditRoleAsync(admin, disabled.Id, ClearanceLevel.TopSecret, "GLOBAL");
        await LegacyUsers.SetEnabledAsync(admin, disabled.Id, enabled: false);
        var self = await ProfileHttp.AccountAsync(vault, VaultFixture.AdminUsername);
        var head = await AuditTrail.HeadAsync(vault);

        var response = await LegacyUsers.EditRoleAsync(admin, self.Id, ClearanceLevel.Secret, "GLOBAL");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Cannot demote the last TopSecret administrator — the vault would become unmanageable.",
            WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, head));
        var stored = await ProfileHttp.AccountAsync(vault, VaultFixture.AdminUsername);
        Assert.Equal((ClearanceLevel.TopSecret, self.CredentialGeneration), (stored.Clearance, stored.CredentialGeneration));
    }
}
