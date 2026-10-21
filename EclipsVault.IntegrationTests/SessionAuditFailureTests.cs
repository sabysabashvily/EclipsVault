using System.Net;
using System.Text.Json;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(AuditFailureCollection.Name)]
public sealed class SessionAuditFailureTests(AuditFailureFixture failure)
{
    [Fact]
    public async Task A_failed_audit_write_answers_revoked_unrecorded_and_the_other_browser_loses_access()
    {
        var vault = failure.Vault;
        var account = await SessionAccount.CreateAsync(vault, "it-sessions-fail-one", "198.51.100.190");
        var other = await account.OpenDeviceAsync(0, "198.51.100.191");
        var id = (await SessionApi.CurrentAsync(other)).SessionId;
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await UiApiRequest.WriteWithAntiforgeryAsync(account.Browser, HttpMethod.Delete, $"/api/ui/sessions/{id}");

            await UiApiFailureContractTests.AssertProblemAsync(response, 503, "revoked_unrecorded");
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("The device was signed out, but the vault couldn't record it in the audit trail. Tell your administrator.", problem.RootElement.GetProperty("title").GetString());
            Assert.False(problem.RootElement.TryGetProperty("signedOut", out _));
            await UiApiFailureContractTests.AssertProblemAsync(await other.Client.GetAsync("/api/ui/me"), 401, "unauthenticated");
            Assert.Equal(HttpStatusCode.OK, (await account.Browser.Client.GetAsync("/api/ui/sessions")).StatusCode);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_failed_bulk_audit_still_signs_out_every_other_browser_and_reports_the_count()
    {
        var vault = failure.Vault;
        var account = await SessionAccount.CreateAsync(vault, "it-sessions-fail-others", "198.51.100.192");
        var first = await account.OpenDeviceAsync(0, "198.51.100.193");
        var second = await account.OpenDeviceAsync(1, "198.51.100.194");
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await UiApiRequest.WriteWithAntiforgeryAsync(account.Browser, HttpMethod.Post, "/api/ui/sessions/sign-out-others");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(["code", "signedOut", "status", "title"], problem.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
            Assert.Equal("revoked_unrecorded", problem.RootElement.GetProperty("code").GetString());
            Assert.Equal(503, problem.RootElement.GetProperty("status").GetInt32());
            Assert.Equal(2, problem.RootElement.GetProperty("signedOut").GetInt32());
            Assert.Equal("The device was signed out, but the vault couldn't record it in the audit trail. Tell your administrator.", problem.RootElement.GetProperty("title").GetString());
            await UiApiFailureContractTests.AssertProblemAsync(await first.Client.GetAsync("/api/ui/me"), 401, "unauthenticated");
            await UiApiFailureContractTests.AssertProblemAsync(await second.Client.GetAsync("/api/ui/me"), 401, "unauthenticated");
            Assert.Equal(HttpStatusCode.OK, (await account.Browser.Client.GetAsync("/api/ui/me")).StatusCode);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }
}
