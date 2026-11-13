using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Auditing;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Persistence.Interceptors;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The Audit log's UI API and page routes. Each test acts as accounts of its own and asserts only on
/// the rows they wrote, since every other test writes to the same trail.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class AuditApiTests(VaultFixture vault)
{
    private const string LogPath = "/api/ui/audit";
    private const string VerifyPath = LogPath + "/verify";
    private const string CheckpointsPath = LogPath + "/checkpoints";
    private const string ExportPath = LogPath + "/export";

    [Theory]
    [InlineData("GET", LogPath, "198.18.143.11")]
    [InlineData("POST", VerifyPath, "198.18.143.12")]
    [InlineData("POST", CheckpointsPath, "198.18.143.13")]
    [InlineData("POST", ExportPath, "198.18.143.14")]
    public async Task Each_endpoint_accepts_only_an_administrator_session(string method, string path, string source)
    {
        var suffix = source.Split('.').Last();
        var reader = await vault.SignInNewAccountAsync("it-audit-reader-" + suffix, IPAddress.Parse(source));
        var key = vault.CreateClient();
        key.DefaultRequestHeaders.Authorization = new("Bearer", await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), "it-audit-key-" + suffix));
        var before = await AuditTrail.HeadAsync(vault);

        await UiApiFailureContractTests.AssertProblemAsync(await vault.CreateClient().SendAsync(new(new(method), path)), 401, "unauthenticated");
        await UiApiFailureContractTests.AssertProblemAsync(
            await UiApiRequest.SendWithTokenAsync(reader.Client, await UiApiRequest.AntiforgeryTokenAsync(reader.Client), new(method), path),
            403, "forbidden");
        await UiApiFailureContractTests.AssertProblemAsync(await key.SendAsync(new(new(method), path)), 401, "unauthenticated");

        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), row => row.Username == "it-audit-reader-" + suffix
            && row.Action is AuditAction.AuditLogViewed or AuditAction.AuditChainVerified or AuditAction.AuditCheckpointCreated
                or AuditAction.AuditBundleExported);
    }

    [Theory]
    [InlineData(VerifyPath, "198.18.143.21")]
    [InlineData(CheckpointsPath, "198.18.143.22")]
    [InlineData(ExportPath, "198.18.143.23")]
    public async Task A_post_without_an_antiforgery_token_is_refused_and_records_nothing(string path, string source)
    {
        var username = "it-audit-unverified-" + source.Split('.').Last();
        var session = await vault.SignInNewAccountAsync(username, IPAddress.Parse(source), ClearanceLevel.TopSecret);
        var before = await AuditTrail.HeadAsync(vault);

        await UiApiFailureContractTests.AssertProblemAsync(await session.Client.PostAsync(path, null), 400, "antiforgery_rejected");

        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), row => row.Username == username);
    }

    [Fact]
    public async Task The_log_answers_labelled_entries_and_the_signing_key_uncached()
    {
        const string viewer = "it-audit-shape";
        var session = await vault.SignInNewAccountAsync(viewer, IPAddress.Parse("198.18.143.31"), ClearanceLevel.TopSecret);

        var response = await session.Client.GetAsync(LogPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["entries", "latestCheckpoint", "signingKeyId"], ProfileApi.NamesOf(body));
        var signingKeyId = vault.Services.GetRequiredService<IAuditCheckpointService>().SigningKeyId;
        Assert.Equal(signingKeyId, body.GetProperty("signingKeyId").GetString());
        var entries = body.GetProperty("entries").EnumerateArray().ToArray();
        Assert.InRange(entries.Length, 1, 200);
        Assert.Equal(
            ["action", "actionLabel", "actor", "details", "entryId", "isCritical", "resource", "sourceIp", "timestampUtc", "tone"],
            ProfileApi.NamesOf(entries[0]));
        var own = Assert.Single(entries, entry => entry.GetProperty("actor").GetString() == viewer
            && entry.GetProperty("action").GetString() == "AuditLogViewed");
        Assert.Equal(
            ("AuditLogViewed", "Audit log viewed", "muted", "AuditTrail", "198.18.143.31", "Opened the newest 200 entries", false),
            (own.GetProperty("action").GetString(), own.GetProperty("actionLabel").GetString(), own.GetProperty("tone").GetString(),
                own.GetProperty("resource").GetString(), own.GetProperty("sourceIp").GetString(), own.GetProperty("details").GetString(),
                own.GetProperty("isCritical").GetBoolean()));
    }

    [Fact]
    public async Task The_latest_checkpoint_is_answered_with_its_sequence_time_and_key()
    {
        var session = await vault.SignInNewAccountAsync("it-audit-latest", IPAddress.Parse("198.18.143.32"), ClearanceLevel.TopSecret);
        var created = await CreateCheckpointAsync(session);

        var latest = (await session.Client.GetFromJsonAsync<JsonElement>(LogPath)).GetProperty("latestCheckpoint");

        // Another test may sign a later checkpoint between the two requests; the latest is never older.
        Assert.Equal(["createdAtUtc", "sequence", "signingKeyId"], ProfileApi.NamesOf(latest));
        Assert.True(latest.GetProperty("sequence").GetInt64() >= created.GetProperty("sequence").GetInt64());
        Assert.Equal(created.GetProperty("signingKeyId").GetString(), latest.GetProperty("signingKeyId").GetString());
    }

    [Fact]
    public async Task Entries_written_at_the_same_time_are_listed_newest_sequence_first()
    {
        const string actor = "it-audit-same-time";
        await using (var scope = vault.Services.CreateAsyncScope())
        {
            // One save chains both rows under a single time, as an account operation does.
            await scope.ServiceProvider.GetRequiredService<AuditSaveChangesInterceptor>().SaveOperationAsync(
                scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>(),
                [Event("it-audit-chained-first"), Event("it-audit-chained-second")], default);
        }

        var entries = (await (await vault.AdminClientAsync()).GetFromJsonAsync<JsonElement>(LogPath)).GetProperty("entries").EnumerateArray()
            .Where(entry => entry.GetProperty("actor").GetString() == actor).ToArray();

        Assert.Equal(["it-audit-chained-second", "it-audit-chained-first"], entries.Select(entry => entry.GetProperty("resource").GetString()));
        Assert.Equal(entries[0].GetProperty("timestampUtc").GetString(), entries[1].GetProperty("timestampUtc").GetString());

        static AuditEntry Event(string resource) => new()
        {
            Action = AuditAction.ProfileUpdated,
            ResourceType = "Test",
            ResourceName = resource,
            ActorUsername = actor
        };
    }

    [Fact]
    public async Task Verifying_answers_the_outcome_and_its_counts_uncached()
    {
        var session = await vault.SignInNewAccountAsync("it-audit-verify-shape", IPAddress.Parse("198.18.143.33"), ClearanceLevel.TopSecret);
        await CreateCheckpointAsync(session);

        var response = await UiApiRequest.WriteWithAntiforgeryAsync(session, HttpMethod.Post, VerifyPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            ["anchoredAtSequence", "checkpointsChecked", "checkpointsNotChecked", "entriesVerified", "firstBrokenSequence", "message", "outcome"],
            ProfileApi.NamesOf(body));
        Assert.Equal("anchored", body.GetProperty("outcome").GetString());
        var anchor = body.GetProperty("anchoredAtSequence").GetInt64();
        Assert.True(body.GetProperty("entriesVerified").GetInt64() >= anchor);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("firstBrokenSequence").ValueKind);
        Assert.True(body.GetProperty("checkpointsChecked").GetInt32() >= 1);
        Assert.StartsWith($"{body.GetProperty("entriesVerified").GetInt64()} entries verified. The signed checkpoint at sequence {anchor} matches",
            body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Creating_a_checkpoint_answers_201_with_what_it_signed()
    {
        var session = await vault.SignInNewAccountAsync("it-audit-checkpoint-shape", IPAddress.Parse("198.18.143.34"), ClearanceLevel.TopSecret);
        var head = await AuditTrail.HeadAsync(vault);

        var response = await UiApiRequest.WriteWithAntiforgeryAsync(session, HttpMethod.Post, CheckpointsPath);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["createdAtUtc", "sequence", "signingKeyId"], ProfileApi.NamesOf(body));
        Assert.True(body.GetProperty("sequence").GetInt64() >= head);
        Assert.Equal(vault.Services.GetRequiredService<IAuditCheckpointService>().SigningKeyId, body.GetProperty("signingKeyId").GetString());
    }

    [Fact]
    public async Task A_decoy_is_named_as_one_only_after_it_trips()
    {
        var observer = await vault.SignInNewAccountAsync("it-audit-decoy-observer", IPAddress.Parse("198.18.143.41"), ClearanceLevel.TopSecret);
        var intruder = await vault.SignInNewAccountAsync("it-audit-decoy-intruder", IPAddress.Parse("198.18.143.42"), ClearanceLevel.TopSecret);
        const string decoy = "it-audit-decoy";
        var details = await LegacySharing.CreateSecretAsync(intruder.Client, decoy);
        await LegacySharing.MakeDecoyAsync(vault, details);

        var before = await EntriesNamingAsync(observer, decoy);
        await intruder.Client.GetAsync(details);
        var after = await EntriesNamingAsync(observer, decoy);

        Assert.Equal(("SecretCreated", "Created", "ok", false), Summary(Assert.Single(before)));
        var tripped = Assert.Single(after, entry => entry.GetProperty("action").GetString() == "HoneyTokenTripped");
        Assert.Equal(("HoneyTokenTripped", "Honey token", "critical", true), Summary(tripped));
        Assert.Equal("it-audit-decoy-intruder", tripped.GetProperty("actor").GetString());

        static (string?, string?, string?, bool) Summary(JsonElement entry) => (entry.GetProperty("action").GetString(),
            entry.GetProperty("actionLabel").GetString(), entry.GetProperty("tone").GetString(), entry.GetProperty("isCritical").GetBoolean());
    }

    [Theory]
    [InlineData("/Audit", "198.18.143.61")]
    [InlineData("/Audit/Index", "198.18.143.62")]
    [InlineData("/aUdIt/iNdEx", "198.18.143.63")]
    public async Task Each_bookmark_keeps_its_return_url_and_serves_Angular_only_to_administrators(string path, string source)
    {
        var anonymous = await vault.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Equal(path, System.Web.HttpUtility.ParseQueryString(anonymous.Headers.Location!.Query)["ReturnUrl"]);
        var reader = await vault.SignInNewAccountAsync("it-audit-page-" + source.Split('.').Last(), IPAddress.Parse(source));
        var denied = await reader.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/Denied", denied.Headers.Location?.AbsolutePath);
        var administrator = "it-audit-page-admin-" + source.Split('.').Last();
        var admin = await vault.SignInNewAccountAsync(administrator, IPAddress.Parse(source), ClearanceLevel.TopSecret);
        var before = await AuditTrail.HeadAsync(vault);

        var document = await admin.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.True(document.Headers.CacheControl?.NoStore);
        Assert.Contains("<app-root>", await document.Content.ReadAsStringAsync());
        Assert.Contains("require-trusted-types-for 'script'", Assert.Single(document.Headers.GetValues("Content-Security-Policy")));
        // The page's own request for the entries is what records the view.
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), row => row.Username == administrator);
    }

    [Theory]
    [InlineData("Critical alerts (24 h)")]
    [InlineData("Full audit log")]
    [InlineData("Audit log")]
    public async Task The_Legacy_sidebar_and_Dashboard_links_keep_their_destination(string text)
    {
        var html = await (await vault.AdminClientAsync()).GetStringAsync("/");
        var link = Assert.Single(Regex.Matches(html, "<a\\b[^>]*>[\\s\\S]*?</a>").Select(match => match.Value),
            anchor => Regex.Replace(anchor, "<[^>]*>", "").Contains(text, StringComparison.Ordinal));

        Assert.Contains("href=\"/Audit\"", link);
    }

    [Theory]
    [InlineData("POST", "/Audit")]
    [InlineData("PUT", "/aUdIt/")]
    [InlineData("POST", "/Audit/Index")]
    [InlineData("DELETE", "/AUDIT/INDEX")]
    public async Task The_page_paths_refuse_every_method_but_GET_and_HEAD(string method, string path)
    {
        var response = await SendAsAdministratorAsync(method, path);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/Audit/Verify")]
    [InlineData("POST", "/Audit/CreateCheckpoint")]
    [InlineData("POST", "/aUdIt/eXpOrT")]
    [InlineData("GET", "/Audit/Export")]
    [InlineData("GET", "/Audit/Index/11111111-1111-1111-1111-111111111111")]
    public async Task The_Legacy_actions_and_deeper_paths_are_not_found(string method, string path)
    {
        var response = await SendAsAdministratorAsync(method, path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<JsonElement> CreateCheckpointAsync(VaultSession session)
    {
        var response = await UiApiRequest.WriteWithAntiforgeryAsync(session, HttpMethod.Post, CheckpointsPath);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement[]> EntriesNamingAsync(VaultSession session, string resource)
        => (await session.Client.GetFromJsonAsync<JsonElement>(LogPath)).GetProperty("entries").EnumerateArray()
            .Where(entry => entry.GetProperty("resource").GetString() == resource).ToArray();

    private async Task<HttpResponseMessage> SendAsAdministratorAsync(string method, string path)
    {
        var admin = await vault.AdminClientAsync();
        return await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin), new HttpMethod(method), path);
    }
}
