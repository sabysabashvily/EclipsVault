using System.Net;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Legacy's Audit actions have no handler left. Each row acts as an administrator of its own, so a
/// handler that answered would also leave that administrator's audit row behind.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class RetiredAuditRoutesTests(VaultFixture vault)
{
    [Theory]
    [InlineData("POST", "/Audit/Verify", "198.18.144.1")]
    [InlineData("GET", "/audit/verify", "198.18.144.2")]
    [InlineData("HEAD", "/Audit/Verify", "198.18.144.3")]
    [InlineData("PUT", "/AUDIT/VERIFY", "198.18.144.4")]
    [InlineData("DELETE", "/aUdIt/VeRiFy/", "198.18.144.5")]
    [InlineData("POST", "/Audit/CreateCheckpoint", "198.18.144.6")]
    [InlineData("GET", "/audit/createcheckpoint", "198.18.144.7")]
    [InlineData("PATCH", "/AUDIT/CREATECHECKPOINT", "198.18.144.8")]
    [InlineData("PUT", "/aUdIt/CrEaTeChEcKpOiNt/", "198.18.144.9")]
    [InlineData("POST", "/Audit/Export", "198.18.144.10")]
    [InlineData("GET", "/aUdIt/eXpOrT", "198.18.144.11")]
    [InlineData("POST", "/AUDIT/EXPORT/", "198.18.144.12")]
    [InlineData("DELETE", "/audit/export", "198.18.144.13")]
    [InlineData("PATCH", "/Audit/Export", "198.18.144.14")]
    public async Task A_retired_Audit_action_in_any_letter_case_or_method_reaches_no_handler(string method, string path, string source)
    {
        var username = "it-retired-audit-" + source.Split('.').Last();
        var administrator = await vault.SignInNewAccountAsync(username, IPAddress.Parse(source), ClearanceLevel.TopSecret);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await UiApiRequest.WriteWithAntiforgeryAsync(administrator, new HttpMethod(method), path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), row => row.Username == username);
    }
}
