using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Extensions;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class DataApiTests(VaultFixture vault)
{
    private const string Notice =
        "This file contains the account and security metadata EclipsVault holds about you. " +
        "It never includes secret values, passwords, authenticator seeds, or backup codes — " +
        "those are encrypted and access-controlled and are never exported.";

    [Theory]
    [InlineData("GET", "/api/ui/data")]
    [InlineData("POST", "/api/ui/data/export")]
    public async Task Each_data_endpoint_refuses_anonymous_and_working_api_key_callers(string method, string path)
    {
        var client = vault.CreateClient();
        await UiApiFailureContractTests.AssertProblemAsync(await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)), 401, "unauthenticated");
        var key = await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), $"it-data-key-{method}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/secrets")).StatusCode);

        await UiApiFailureContractTests.AssertProblemAsync(await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)), 401, "unauthenticated");
    }

    [Fact]
    public async Task The_summary_pins_the_complete_wire_shape_and_writes_no_audit_row()
    {
        var account = await SessionAccount.CreateAsync(vault, "it-data-summary", "198.51.100.200", ClearanceLevel.Elevated);
        await account.OpenDeviceAsync(0, "198.51.100.201");
        var before = await LegacySharing.AuditHeadAsync(vault);

        var response = await account.Browser.Client.GetAsync("/api/ui/data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var summary = document.RootElement;
        Assert.Equal(
            ["accessRequestCount", "account", "activityCount", "backupCodesRemaining", "deviceCount", "notice", "passkeyCount", "twoStepEnabled"],
            NamesOf(summary));
        var shown = summary.GetProperty("account");
        Assert.Equal(["clearance", "displayName", "email", "hasCustomAvatar", "projectKey", "username"], NamesOf(shown));
        Assert.Equal("it-data-summary", shown.GetProperty("username").GetString());
        Assert.Equal("Integration Test", shown.GetProperty("displayName").GetString());
        Assert.StartsWith("integration.test.", shown.GetProperty("email").GetString());
        Assert.Equal("Elevated", shown.GetProperty("clearance").GetString());
        Assert.Equal("GLOBAL", shown.GetProperty("projectKey").GetString());
        Assert.False(shown.GetProperty("hasCustomAvatar").GetBoolean());
        Assert.True(summary.GetProperty("twoStepEnabled").GetBoolean());
        Assert.Equal(0, summary.GetProperty("passkeyCount").GetInt32());
        Assert.Equal(9, summary.GetProperty("backupCodesRemaining").GetInt32());
        Assert.Equal(2, summary.GetProperty("deviceCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("accessRequestCount").GetInt32());
        Assert.True(summary.GetProperty("activityCount").GetInt32() > 0, "The account's own sign-ins are its activity.");
        Assert.Equal(Notice, summary.GetProperty("notice").GetString());
        Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task The_export_sends_the_legacy_file_with_its_name_cache_headers_and_pinned_shape()
    {
        // Leading and trailing hyphens are valid in a username but are trimmed from the file name.
        var account = await SessionAccount.CreateAsync(vault, "-it-data-file-", "198.51.100.202");
        await account.OpenDeviceAsync(0, "198.51.100.203");
        using var summary = JsonDocument.Parse(await account.Browser.Client.GetStringAsync("/api/ui/data"));

        var response = await SessionApi.WriteAsync(account.Browser, HttpMethod.Post, "/api/ui/data/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store, no-cache, must-revalidate", Assert.Single(response.Headers.NonValidated["Cache-Control"]));
        Assert.Equal("no-cache", Assert.Single(response.Headers.NonValidated["Pragma"]));
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("{\n  \"GeneratedAtUtc\": ", body.ReplaceLineEndings("\n"));
        using var file = JsonDocument.Parse(body);
        var export = file.RootElement;
        var generated = DateTimeOffset.Parse(export.GetProperty("GeneratedAtUtc").GetString()!, CultureInfo.InvariantCulture);
        var disposition = Assert.IsType<ContentDispositionHeaderValue>(response.Content.Headers.ContentDisposition);
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Equal($"eclipsvault-data-it-data-file-{generated.UtcDateTime:yyyyMMdd}.json", disposition.FileName);
        Assert.Equal(
            ["AccessRequests", "Account", "GeneratedAtUtc", "Notice", "RecentActivity", "SchemaVersion", "Security", "SignedInDevices"],
            NamesOf(export));
        Assert.Equal("1.0", export.GetProperty("SchemaVersion").GetString());
        Assert.Equal(Notice, export.GetProperty("Notice").GetString());
        var exportedAccount = export.GetProperty("Account");
        Assert.Equal(["Clearance", "DisplayName", "Email", "HasCustomAvatar", "ProjectKey", "Username"], NamesOf(exportedAccount));
        Assert.Equal("-it-data-file-", exportedAccount.GetProperty("Username").GetString());
        Assert.Equal(summary.RootElement.GetProperty("account").GetProperty("email").GetString(), exportedAccount.GetProperty("Email").GetString());
        Assert.Equal("Standard", exportedAccount.GetProperty("Clearance").GetString());
        var security = export.GetProperty("Security");
        Assert.Equal(["BackupCodesRemaining", "Passkeys", "TwoStepEnabled"], NamesOf(security));
        Assert.Equal(9, security.GetProperty("BackupCodesRemaining").GetInt32());
        Assert.True(security.GetProperty("TwoStepEnabled").GetBoolean());
        Assert.Empty(security.GetProperty("Passkeys").EnumerateArray());
        var devices = export.GetProperty("SignedInDevices").EnumerateArray().ToArray();
        Assert.Equal(["198.51.100.202", "198.51.100.203"], devices.Select(device => device.GetProperty("IpAddress").GetString()).Order(StringComparer.Ordinal));
        Assert.All(devices, device => Assert.Equal(["Device", "IpAddress", "LastActiveAtUtc", "SignedInAtUtc"], NamesOf(device)));
        Assert.Empty(export.GetProperty("AccessRequests").EnumerateArray());
        var activity = export.GetProperty("RecentActivity").EnumerateArray().ToArray();
        // How many rows the account's sign-ins wrote is the vault's detail; the page must show the file's count.
        Assert.Equal(summary.RootElement.GetProperty("activityCount").GetInt32(), activity.Length);
        Assert.All(activity, entry => Assert.Equal(["Action", "Resource", "SourceIp", "TimestampUtc"], NamesOf(entry)));
    }

    [Fact]
    public async Task The_export_records_exactly_one_PersonalDataExported_row_naming_its_activity_count()
    {
        var session = await vault.SignInNewAccountAsync("it-data-audited", IPAddress.Parse("198.51.100.210"));
        var userId = SessionApi.PrincipalOf(vault, session).GetUserId();
        var before = await LegacySharing.AuditHeadAsync(vault);

        var response = await SessionApi.WriteAsync(session, HttpMethod.Post, "/api/ui/data/export");

        using var file = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var activityCount = file.RootElement.GetProperty("RecentActivity").GetArrayLength();
        var row = Assert.Single(await LegacySharing.AuditRowsAfterAsync(vault, before));
        Assert.Equal(AuditAction.PersonalDataExported, row.Action);
        Assert.Equal("Account", row.ResourceType);
        Assert.Equal(userId, row.ResourceId);
        Assert.Equal(userId, row.UserId);
        Assert.Equal("198.51.100.210", row.SourceIp);
        // The count depends on how many sign-in steps wrote activity rows, so it is read from the file the row describes.
        Assert.Equal($"Exported personal data ({activityCount} activity entries)", row.Details);
    }

    [Fact]
    public async Task Two_accounts_see_and_export_only_their_own_data_even_with_injected_account_parameters()
    {
        var a = await vault.SignInNewAccountAsync("it-data-scope-a", IPAddress.Parse("198.51.100.204"));
        var b = await vault.SignInNewAccountAsync("it-data-scope-b", IPAddress.Parse("198.51.100.205"));
        var aUserId = SessionApi.PrincipalOf(vault, a).GetUserId();
        var bUserId = SessionApi.PrincipalOf(vault, b).GetUserId();
        var aQuery = $"?userId={bUserId}&accountId={bUserId}&username=it-data-scope-b";
        var bQuery = $"?userId={aUserId}&accountId={aUserId}&username=it-data-scope-a";

        var aSummary = await a.Client.GetStringAsync("/api/ui/data");
        var bSummary = await b.Client.GetStringAsync("/api/ui/data");
        Assert.Equal(aSummary, await a.Client.GetStringAsync("/api/ui/data" + aQuery));
        Assert.Equal(bSummary, await b.Client.GetStringAsync("/api/ui/data" + bQuery));
        Assert.Contains("\"username\":\"it-data-scope-a\"", aSummary);
        Assert.DoesNotContain("it-data-scope-b", aSummary);
        Assert.Contains("\"username\":\"it-data-scope-b\"", bSummary);
        Assert.DoesNotContain("it-data-scope-a", bSummary);
        var before = await LegacySharing.AuditHeadAsync(vault);

        var aFile = await (await SessionApi.WriteAsync(a, HttpMethod.Post, "/api/ui/data/export" + aQuery)).Content.ReadAsStringAsync();
        var bFile = await (await SessionApi.WriteAsync(b, HttpMethod.Post, "/api/ui/data/export" + bQuery)).Content.ReadAsStringAsync();

        Assert.Contains("\"Username\": \"it-data-scope-a\"", aFile);
        Assert.Contains("198.51.100.204", aFile);
        Assert.DoesNotContain("it-data-scope-b", aFile);
        Assert.DoesNotContain("198.51.100.205", aFile);
        Assert.Contains("\"Username\": \"it-data-scope-b\"", bFile);
        Assert.Contains("198.51.100.205", bFile);
        Assert.DoesNotContain("it-data-scope-a", bFile);
        Assert.DoesNotContain("198.51.100.204", bFile);
        var rows = await LegacySharing.AuditRowsAfterAsync(vault, before);
        Assert.Equal([aUserId, bUserId], rows.Select(row => row.UserId));
        Assert.Equal([aUserId, bUserId], rows.Select(row => row.ResourceId));
        Assert.All(rows, row => Assert.Equal(AuditAction.PersonalDataExported, row.Action));
    }

    [Fact]
    public async Task The_export_requires_the_antiforgery_header_and_sends_no_file_without_it()
    {
        var session = await vault.SignInNewAccountAsync("it-data-csrf", IPAddress.Parse("198.51.100.206"));
        var before = await LegacySharing.AuditHeadAsync(vault);

        var response = await session.Client.PostAsync("/api/ui/data/export", null);

        await UiApiFailureContractTests.AssertProblemAsync(response, 400, "antiforgery_rejected");
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task A_deleted_accounts_session_is_refused_before_reading_or_exporting_its_data()
    {
        const string username = "it-data-deleted";
        var source = IPAddress.Parse("198.51.100.207");
        var session = await vault.SignInNewAccountAsync(username, source);
        var cookie = Assert.IsType<Cookie>(session.SessionCookie);
        await vault.DeleteAccountAsync(username);
        var before = await LegacySharing.AuditHeadAsync(vault);

        await UiApiFailureContractTests.AssertProblemAsync(await session.Client.GetAsync("/api/ui/data"), 401, "unauthenticated");
        var copy = vault.CreateSession(cookie, source);
        await UiApiFailureContractTests.AssertProblemAsync(await copy.Client.PostAsync("/api/ui/data/export", null), 401, "unauthenticated");
        Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task A_session_without_its_account_id_is_refused_before_reading_or_exporting_its_data()
    {
        var source = IPAddress.Parse("198.51.100.208");
        var original = await vault.SignInNewAccountAsync("it-data-no-account-id", source);
        var cookie = SessionApi.WithoutAccountId(vault, original);
        var before = await LegacySharing.AuditHeadAsync(vault);

        var reader = vault.CreateSession(cookie, source);
        await UiApiFailureContractTests.AssertProblemAsync(await reader.Client.GetAsync("/api/ui/data"), 401, "unauthenticated");
        var exporter = vault.CreateSession(cookie, source);
        await UiApiFailureContractTests.AssertProblemAsync(await exporter.Client.PostAsync("/api/ui/data/export", null), 401, "unauthenticated");
        Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
    }

    private static string[] NamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
}
