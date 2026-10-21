using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Sessions;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models.Ui;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class SessionApiTests(VaultFixture vault)
{
    [Theory]
    [InlineData("GET", "/api/ui/sessions")]
    [InlineData("DELETE", "/api/ui/sessions/11111111-1111-1111-1111-111111111111")]
    [InlineData("POST", "/api/ui/sessions/sign-out-others")]
    public async Task Each_session_endpoint_refuses_anonymous_and_working_api_key_callers(string method, string path)
    {
        var client = vault.CreateClient();
        await UiApiFailureContractTests.AssertProblemAsync(await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)), 401, "unauthenticated");
        var key = await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), $"it-sessions-key-{method}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/secrets")).StatusCode);

        await UiApiFailureContractTests.AssertProblemAsync(await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)), 401, "unauthenticated");
    }

    [Fact]
    public async Task The_session_list_returns_the_callers_devices_as_page_data()
    {
        var started = DateTime.UtcNow.AddSeconds(-1);
        var account = await SessionAccount.CreateAsync(vault, "it-sessions-contract", "198.51.100.170");
        var second = await account.OpenDeviceAsync(0, "198.51.100.171");
        var secondId = (await SessionApi.CurrentAsync(second)).SessionId;

        var response = await account.Browser.Client.GetAsync("/api/ui/sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["sessions"], NamesOf(document.RootElement));
        var devices = document.RootElement.GetProperty("sessions").EnumerateArray().ToArray();
        Assert.Equal(2, devices.Length);
        Assert.All(devices, device => Assert.Equal(
            ["createdAtUtc", "device", "ipAddress", "isCurrent", "lastSeenAtUtc", "sessionId"], NamesOf(device)));
        Assert.Equal(secondId, devices[0].GetProperty("sessionId").GetGuid());
        Assert.Equal("curl", devices[0].GetProperty("device").GetString());
        Assert.Equal("198.51.100.171", devices[0].GetProperty("ipAddress").GetString());
        Assert.False(devices[0].GetProperty("isCurrent").GetBoolean());
        Assert.True(devices[1].GetProperty("isCurrent").GetBoolean());
        Assert.Equal("198.51.100.170", devices[1].GetProperty("ipAddress").GetString());
        Assert.All(devices, device => AssertUtcSince(started, device.GetProperty("createdAtUtc")));
        Assert.All(devices, device => AssertUtcSince(started, device.GetProperty("lastSeenAtUtc")));
    }

    [Fact]
    public async Task Two_accounts_list_and_revoke_only_their_own_devices_even_with_injected_account_parameters()
    {
        var account = await SessionAccount.CreateAsync(vault, "it-sessions-scope-a", "198.51.100.172");
        var otherAccount = await SessionAccount.CreateAsync(vault, "it-sessions-scope-b", "198.51.100.173");
        var device = await account.OpenDeviceAsync(0, "198.51.100.174");
        var otherDevice = await otherAccount.OpenDeviceAsync(0, "198.51.100.175");
        var sessionId = (await SessionApi.CurrentAsync(device)).SessionId;
        var otherSessionId = (await SessionApi.CurrentAsync(otherDevice)).SessionId;
        var accountId = SessionApi.PrincipalOf(vault, account.Browser).GetUserId();
        var otherAccountId = SessionApi.PrincipalOf(vault, otherAccount.Browser).GetUserId();
        var claimingOtherAccount = $"?userId={otherAccountId}&accountId={otherAccountId}&username=it-sessions-scope-b&issuer={otherAccountId}";
        var claimingAccount = $"?userId={accountId}&accountId={accountId}&username=it-sessions-scope-a&issuer={accountId}";

        var listedDevices = await account.Browser.Client.GetStringAsync("/api/ui/sessions");
        var otherListedDevices = await otherAccount.Browser.Client.GetStringAsync("/api/ui/sessions");
        Assert.Equal(listedDevices, await account.Browser.Client.GetStringAsync("/api/ui/sessions" + claimingOtherAccount));
        Assert.Equal(otherListedDevices, await otherAccount.Browser.Client.GetStringAsync("/api/ui/sessions" + claimingAccount));
        Assert.Contains("198.51.100.174", listedDevices);
        Assert.DoesNotContain("198.51.100.175", listedDevices);
        Assert.Contains("198.51.100.175", otherListedDevices);
        Assert.DoesNotContain("198.51.100.174", otherListedDevices);
        var before = await LegacySharing.AuditHeadAsync(vault);

        await UiApiFailureContractTests.AssertProblemAsync(await SessionApi.WriteAsync(account.Browser, HttpMethod.Delete, $"/api/ui/sessions/{otherSessionId}" + claimingOtherAccount), 404, "session_not_found");
        await UiApiFailureContractTests.AssertProblemAsync(await SessionApi.WriteAsync(otherAccount.Browser, HttpMethod.Delete, $"/api/ui/sessions/{sessionId}" + claimingAccount), 404, "session_not_found");
        Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
        Assert.Equal(HttpStatusCode.OK, (await device.Client.GetAsync("/api/ui/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otherDevice.Client.GetAsync("/api/ui/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SessionApi.WriteAsync(account.Browser, HttpMethod.Delete, $"/api/ui/sessions/{sessionId}" + claimingOtherAccount)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SessionApi.WriteAsync(otherAccount.Browser, HttpMethod.Delete, $"/api/ui/sessions/{otherSessionId}" + claimingAccount)).StatusCode);
        await UiApiFailureContractTests.AssertProblemAsync(await device.Client.GetAsync("/api/ui/me"), 401, "unauthenticated");
        await UiApiFailureContractTests.AssertProblemAsync(await otherDevice.Client.GetAsync("/api/ui/me"), 401, "unauthenticated");
        var rows = await LegacySharing.AuditRowsAfterAsync(vault, before);
        Assert.Equal(2, rows.Length);
        Assert.Equal([accountId, otherAccountId], rows.Select(row => row.UserId));
        Assert.Equal([sessionId, otherSessionId], rows.Select(row => row.ResourceId));
        Assert.All(rows, row => Assert.Equal(AuditAction.SessionRevokedByUser, row.Action));
    }

    [Fact]
    public async Task An_administrator_has_no_wider_session_scope()
    {
        var owner = await SessionAccount.CreateAsync(vault, "it-sessions-admin-target", "198.51.100.176");
        var administrator = await SessionAccount.CreateAsync(vault, "it-sessions-admin", "198.51.100.177", ClearanceLevel.TopSecret);
        var target = (await SessionApi.CurrentAsync(owner.Browser)).SessionId;
        var adminList = await administrator.Browser.Client.GetStringAsync("/api/ui/sessions");
        Assert.DoesNotContain("198.51.100.176", adminList);
        Assert.Contains("198.51.100.177", adminList);
        var before = await LegacySharing.AuditHeadAsync(vault);

        await UiApiFailureContractTests.AssertProblemAsync(await SessionApi.WriteAsync(administrator.Browser, HttpMethod.Delete, $"/api/ui/sessions/{target}"), 404, "session_not_found");

        Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
        Assert.Equal(HttpStatusCode.OK, (await owner.Browser.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task The_current_session_is_refused_with_the_legacy_text_and_no_audit_row()
    {
        var session = await vault.SignInNewAccountAsync("it-sessions-current", IPAddress.Parse("198.51.100.178"));
        var current = (await SessionApi.CurrentAsync(session)).SessionId;
        var before = await LegacySharing.AuditHeadAsync(vault);

        var response = await SessionApi.WriteAsync(session, HttpMethod.Delete, $"/api/ui/sessions/{current}");

        await UiApiFailureContractTests.AssertProblemAsync(response, 409, "current_session");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("That's the device you're using now — use Sign out for this one.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(["code", "status", "title"], NamesOf(problem.RootElement));
        Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task Unknown_and_already_revoked_sessions_write_no_audit_row()
    {
        var account = await SessionAccount.CreateAsync(vault, "it-sessions-missing", "198.51.100.179");
        var other = await account.OpenDeviceAsync(0, "198.51.100.180");
        var id = (await SessionApi.CurrentAsync(other)).SessionId;
        var signedOut = await SessionApi.WriteAsync(account.Browser, HttpMethod.Delete, $"/api/ui/sessions/{id}");
        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        Assert.True(signedOut.Headers.CacheControl?.NoStore);
        Assert.Empty(await signedOut.Content.ReadAsStringAsync());
        var before = await LegacySharing.AuditHeadAsync(vault);

        var unknown = await SessionApi.WriteAsync(account.Browser, HttpMethod.Delete, $"/api/ui/sessions/{Guid.NewGuid()}");
        var repeated = await SessionApi.WriteAsync(account.Browser, HttpMethod.Delete, $"/api/ui/sessions/{id}");

        await UiApiFailureContractTests.AssertProblemAsync(unknown, 404, "session_not_found");
        await UiApiFailureContractTests.AssertProblemAsync(repeated, 404, "session_not_found");
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await repeated.Content.ReadAsStringAsync());
        Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task Expired_metadata_answers_not_found_without_audit_but_the_copied_cookie_is_still_revoked()
    {
        var account = await SessionAccount.CreateAsync(vault, "it-sessions-expired", "198.51.100.181");
        var other = await account.OpenDeviceAsync(0, "198.51.100.182");
        var device = await SessionApi.CurrentAsync(other);
        var userId = SessionApi.PrincipalOf(vault, account.Browser).GetUserId();
        var registry = vault.Services.GetRequiredService<ISessionRegistry>();
        await registry.RecordSeenAsync(new SessionObservation(userId, device.SessionId, device.Device, device.IpAddress,
            new DateTimeOffset(device.LastSeenAtUtc).AddMinutes(1), DateTimeOffset.UtcNow.AddSeconds(-1)));
        var before = await LegacySharing.AuditHeadAsync(vault);

        await UiApiFailureContractTests.AssertProblemAsync(await SessionApi.WriteAsync(account.Browser, HttpMethod.Delete, $"/api/ui/sessions/{device.SessionId}"), 404, "session_not_found");

        Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
        var refreshed = await account.Browser.Client.GetFromJsonAsync<SessionsResponse>("/api/ui/sessions");
        Assert.Single(Assert.IsType<SessionsResponse>(refreshed).Sessions);
        await UiApiFailureContractTests.AssertProblemAsync(await other.Client.GetAsync("/api/ui/me"), 401, "unauthenticated");
    }

    [Theory]
    [InlineData("DELETE", "/api/ui/sessions/11111111-1111-1111-1111-111111111111", "198.51.100.183")]
    [InlineData("POST", "/api/ui/sessions/sign-out-others", "198.51.100.184")]
    public async Task Session_writes_require_the_antiforgery_header(string method, string path, string address)
    {
        var session = await vault.SignInNewAccountAsync($"it-sessions-csrf-{method}", IPAddress.Parse(address));
        var before = await LegacySharing.AuditHeadAsync(vault);

        var response = await session.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        await UiApiFailureContractTests.AssertProblemAsync(response, 400, "antiforgery_rejected");
        Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task Signing_out_others_counts_and_audits_only_the_callers_other_devices()
    {
        var account = await SessionAccount.CreateAsync(vault, "it-sessions-others", "198.51.100.185");
        var first = await account.OpenDeviceAsync(0, "198.51.100.186");
        var second = await account.OpenDeviceAsync(1, "198.51.100.187");
        var outsider = await vault.SignInNewAccountAsync("it-sessions-outsider", IPAddress.Parse("198.51.100.188"));
        var firstId = (await SessionApi.CurrentAsync(first)).SessionId;
        var secondId = (await SessionApi.CurrentAsync(second)).SessionId;
        var before = await LegacySharing.AuditHeadAsync(vault);

        var response = await SessionApi.WriteAsync(account.Browser, HttpMethod.Post, "/api/ui/sessions/sign-out-others?username=it-sessions-outsider&userId=" + SessionApi.PrincipalOf(vault, outsider).GetUserId());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("{\"signedOut\":2}", await response.Content.ReadAsStringAsync());
        await UiApiFailureContractTests.AssertProblemAsync(await first.Client.GetAsync("/api/ui/me"), 401, "unauthenticated");
        await UiApiFailureContractTests.AssertProblemAsync(await second.Client.GetAsync("/api/ui/me"), 401, "unauthenticated");
        Assert.Equal(HttpStatusCode.OK, (await account.Browser.Client.GetAsync("/api/ui/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await outsider.Client.GetAsync("/api/ui/me")).StatusCode);
        var rows = await LegacySharing.AuditRowsAfterAsync(vault, before);
        Assert.Equal(2, rows.Length);
        Assert.Contains(rows, row => row.ResourceId == firstId);
        Assert.Contains(rows, row => row.ResourceId == secondId);
        Assert.All(rows, row =>
        {
            Assert.Equal(AuditAction.SessionRevokedByUser, row.Action);
            Assert.Equal("Session", row.ResourceType);
            Assert.Equal($"Revoked session {row.ResourceId:N}", row.Details);
            Assert.Equal("it-sessions-others", row.Username);
            Assert.Equal("198.51.100.185", row.SourceIp);
        });
    }

    [Fact]
    public async Task Signing_out_others_with_no_other_devices_reports_zero_and_writes_no_audit_row()
    {
        var session = await vault.SignInNewAccountAsync("it-sessions-no-others", IPAddress.Parse("198.51.100.196"));
        var before = await LegacySharing.AuditHeadAsync(vault);

        var response = await SessionApi.WriteAsync(session, HttpMethod.Post, "/api/ui/sessions/sign-out-others");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("{\"signedOut\":0}", await response.Content.ReadAsStringAsync());
        Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task A_deleted_accounts_session_is_refused_before_listing_or_revoking_devices()
    {
        const string username = "it-sessions-deleted";
        var session = await vault.SignInNewAccountAsync(username, IPAddress.Parse("198.51.100.189"));
        var cookie = Assert.IsType<Cookie>(session.SessionCookie);
        await vault.DeleteAccountAsync(username);

        await UiApiFailureContractTests.AssertProblemAsync(await session.Client.GetAsync("/api/ui/sessions"), 401, "unauthenticated");
        var copy = vault.CreateSession(cookie, IPAddress.Parse("198.51.100.189"));
        await UiApiFailureContractTests.AssertProblemAsync(await copy.Client.DeleteAsync($"/api/ui/sessions/{Guid.NewGuid()}"), 401, "unauthenticated");
    }

    [Fact]
    public async Task A_session_without_its_account_id_is_refused_before_any_session_action()
    {
        var source = IPAddress.Parse("198.51.100.195");
        var original = await vault.SignInNewAccountAsync("it-sessions-no-account-id", source);
        var cookie = SessionApi.WithoutAccountId(vault, original);
        var before = await LegacySharing.AuditHeadAsync(vault);

        var reader = vault.CreateSession(cookie, source);
        await UiApiFailureContractTests.AssertProblemAsync(await reader.Client.GetAsync("/api/ui/sessions"), 401, "unauthenticated");
        var single = vault.CreateSession(cookie, source);
        await UiApiFailureContractTests.AssertProblemAsync(await single.Client.DeleteAsync($"/api/ui/sessions/{Guid.NewGuid()}"), 401, "unauthenticated");
        var bulk = vault.CreateSession(cookie, source);
        await UiApiFailureContractTests.AssertProblemAsync(await bulk.Client.PostAsync("/api/ui/sessions/sign-out-others", null), 401, "unauthenticated");
        Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
    }

    private static string[] NamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static void AssertUtcSince(DateTime started, JsonElement timestamp)
    {
        var text = timestamp.GetString()!;
        Assert.EndsWith("Z", text);
        Assert.InRange(DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), started, DateTime.UtcNow.AddSeconds(1));
    }
}
