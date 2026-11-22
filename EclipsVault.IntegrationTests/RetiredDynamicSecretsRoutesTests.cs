using System.Net;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Dynamic secrets is the Angular UI's. The Legacy page's Issue and Revoke stay unreachable for the
/// holder, with a real session and a valid antiforgery token, aiming at a real role and an active lease.
/// </summary>
[Collection(DynamicSecretsCollection.Name)]
public sealed class RetiredDynamicSecretsRoutesTests(DynamicSecretsFixture fixture)
{
    private readonly VaultFixture vault = fixture.Vault;

    // A literal path the Angular UI owns answers 405 to anything but GET and HEAD; a removed action has no
    // route at all, so it answers 404.
    [Theory]
    [InlineData("POST", "/DynamicSecrets/Issue", HttpStatusCode.NotFound)]
    [InlineData("POST", "/dYnAmIcSeCrEtS/iSsUe/", HttpStatusCode.NotFound)]
    [InlineData("GET", "/dynamicsecrets/issue", HttpStatusCode.NotFound)]
    [InlineData("POST", "/DynamicSecrets/Revoke", HttpStatusCode.NotFound)]
    [InlineData("PUT", "/DYNAMICSECRETS/REVOKE", HttpStatusCode.NotFound)]
    [InlineData("POST", "/DynamicSecrets", HttpStatusCode.MethodNotAllowed)]
    [InlineData("POST", "/dynamicsecrets/index", HttpStatusCode.MethodNotAllowed)]
    public async Task A_retired_dynamic_secrets_route_reaches_no_handler(string method, string path, HttpStatusCode expected)
    {
        var roleName = $"it_dyn_retired_{Guid.NewGuid():N}"[..28];
        var roleId = await DynamicSecretsApi.AddRoleAsync(vault, roleName);
        var admin = await vault.AdminClientAsync();
        (await DynamicSecretsApi.IssueAsync(admin, roleId)).EnsureSuccessStatusCode();
        var lease = Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId));
        var before = await AuditTrail.HeadAsync(vault);

        using var response = await UiApiRequest.SendWithTokenAsync(admin, await DynamicSecretsApi.TokenAsync(admin), new HttpMethod(method),
            $"{path}?id={lease.Id}", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["RoleId"] = roleId.ToString(),
                ["TtlMinutes"] = "5",
                ["id"] = lease.Id.ToString(),
            }));

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(LeaseStatus.Active, Assert.Single(await DynamicSecretsApi.LeasesOfRoleAsync(vault, roleId)).Status);
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), row => row.ResourceId == lease.Id || row.ResourceId == roleId);
        await DynamicSecretsApi.RevokeAsync(admin, lease.Id);
    }
}
