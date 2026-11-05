using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Web.Models.Ui;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class ServiceAccountsApiTests(VaultFixture vault)
{
    private const string Missing = "/api/ui/service-accounts/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string MissingKey = Missing + "/keys/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    [Theory]
    [InlineData("GET", "/api/ui/service-accounts", "198.18.111.11")]
    [InlineData("GET", "/api/ui/service-accounts/new", "198.18.111.12")]
    [InlineData("POST", "/api/ui/service-accounts", "198.18.111.13")]
    [InlineData("GET", Missing, "198.18.111.14")]
    [InlineData("POST", Missing + "/keys", "198.18.111.15")]
    [InlineData("DELETE", MissingKey, "198.18.111.16")]
    [InlineData("POST", Missing + "/enable", "198.18.111.17")]
    [InlineData("POST", Missing + "/disable", "198.18.111.18")]
    [InlineData("DELETE", Missing, "198.18.111.19")]
    public async Task Each_endpoint_accepts_only_an_administrator_session(string method, string path, string source)
    {
        var anonymous = vault.CreateClient();
        var reader = await vault.SignInNewAccountAsync("it-sa-reader-" + source.Split('.').Last(), IPAddress.Parse(source));
        var keyClient = vault.CreateClient();
        keyClient.DefaultRequestHeaders.Authorization = new("Bearer", await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), "it-sa-auth-key-" + source.Split('.').Last()));

        await UiApiFailureContractTests.AssertProblemAsync(await anonymous.SendAsync(new(new(method), path)), 401, "unauthenticated");
        await UiApiFailureContractTests.AssertProblemAsync(await reader.Client.SendAsync(new(new(method), path)), 403, "forbidden");
        await UiApiFailureContractTests.AssertProblemAsync(await keyClient.SendAsync(new(new(method), path)), 401, "unauthenticated");
    }

    [Theory]
    [InlineData("POST", "/api/ui/service-accounts")]
    [InlineData("POST", Missing + "/keys")]
    [InlineData("DELETE", MissingKey)]
    [InlineData("POST", Missing + "/enable")]
    [InlineData("POST", Missing + "/disable")]
    [InlineData("DELETE", Missing)]
    public async Task Every_write_requires_antiforgery(string method, string path)
    {
        var admin = await vault.AdminClientAsync();
        await UiApiFailureContractTests.AssertProblemAsync(await admin.SendAsync(new(new(method), path)
        { Content = JsonContent.Create(new { expiresInDays = 0 }) }), 400, "antiforgery_rejected");
    }

    [Theory]
    [InlineData("GET", Missing, "service_account_not_found")]
    [InlineData("POST", Missing + "/keys", "service_account_not_found")]
    [InlineData("DELETE", MissingKey, "key_not_found")]
    [InlineData("POST", Missing + "/enable", "service_account_not_found")]
    [InlineData("POST", Missing + "/disable", "service_account_not_found")]
    [InlineData("DELETE", Missing, "service_account_not_found")]
    public async Task Unknown_targets_have_the_agreed_problem(string method, string path, string code)
    {
        var admin = await vault.AdminClientAsync();
        await UiApiFailureContractTests.AssertProblemAsync(await ServiceAccountsApi.WriteAsync(admin, new(method), path,
            JsonContent.Create(new { expiresInDays = 0 })), 404, code);
    }

    [Theory]
    [InlineData("keys", "198.18.111.21")]
    [InlineData("enable", "198.18.111.22")]
    public async Task Stale_grants_require_proof_before_lookup(string action, string source)
    {
        var admin = await vault.SignInNewAccountAsync("it-sa-order-" + action, IPAddress.Parse(source), ClearanceLevel.TopSecret);
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, admin, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse(source));
        await AdministratorFreshnessTests.AssertStepUpAsync(await ServiceAccountsApi.WriteAsync(stale.Client, HttpMethod.Post, Missing + "/" + action,
            JsonContent.Create(new { expiresInDays = 0 })), "code");
    }

    [Fact]
    public async Task A_stale_administrator_can_create_a_service_account_without_fresh_proof()
    {
        var admin = await vault.SignInNewAccountAsync("it-sa-stale-create-admin", IPAddress.Parse("198.19.112.11"), ClearanceLevel.TopSecret);
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, admin, DateTimeOffset.UtcNow.AddDays(-1)), IPAddress.Parse("198.19.112.11"));

        Assert.Equal(HttpStatusCode.Created, (await ServiceAccountsApi.CreateAsync(stale.Client, "it-sa-create-stale")).StatusCode);
        Assert.False((await ServiceAccountsApi.FindAsync(vault, "it-sa-create-stale"))!.IsDisabled);
    }

    [Fact]
    public async Task A_stale_administrator_can_disable_a_service_account_without_fresh_proof()
    {
        var admin = await vault.SignInNewAccountAsync("it-sa-stale-disable-admin", IPAddress.Parse("198.19.112.12"), ClearanceLevel.TopSecret);
        var account = await ServiceAccountsApi.CreatedAsync(vault, admin.Client, "it-sa-stale-disable");
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, admin, DateTimeOffset.UtcNow.AddDays(-1)), IPAddress.Parse("198.19.112.12"));

        Assert.Equal(HttpStatusCode.NoContent, (await ServiceAccountsApi.SetEnabledAsync(stale.Client, account.Id, false)).StatusCode);
        Assert.True((await ServiceAccountsApi.FindAsync(vault, "it-sa-stale-disable"))!.IsDisabled);
    }

    [Fact]
    public async Task A_stale_administrator_can_revoke_a_key_without_fresh_proof()
    {
        var admin = await vault.SignInNewAccountAsync("it-sa-stale-revoke-admin", IPAddress.Parse("198.19.112.13"), ClearanceLevel.TopSecret);
        var account = await ServiceAccountsApi.CreatedAsync(vault, admin.Client, "it-sa-stale-revoke");
        var key = await ServiceAccountsApi.IssuedAsync(admin.Client, account.Id);
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, admin, DateTimeOffset.UtcNow.AddDays(-1)), IPAddress.Parse("198.19.112.13"));

        Assert.Equal(HttpStatusCode.NoContent, (await ServiceAccountsApi.RevokeKeyAsync(stale.Client, account.Id, key.KeyId)).StatusCode);
        Assert.NotNull(Assert.Single((await ServiceAccountsApi.FindAsync(vault, "it-sa-stale-revoke"))!.Keys).RevokedAtUtc);
    }

    [Fact]
    public async Task A_stale_administrator_can_delete_a_service_account_without_fresh_proof()
    {
        var admin = await vault.SignInNewAccountAsync("it-sa-stale-delete-admin", IPAddress.Parse("198.19.112.14"), ClearanceLevel.TopSecret);
        var account = await ServiceAccountsApi.CreatedAsync(vault, admin.Client, "it-sa-stale-delete");
        await ServiceAccountsApi.IssuedAsync(admin.Client, account.Id);
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, admin, DateTimeOffset.UtcNow.AddDays(-1)), IPAddress.Parse("198.19.112.14"));

        Assert.Equal(HttpStatusCode.NoContent, (await ServiceAccountsApi.DeleteAsync(stale.Client, account.Id)).StatusCode);
        Assert.Null(await ServiceAccountsApi.FindAsync(vault, "it-sa-stale-delete"));
    }

    [Fact]
    public async Task Fresh_proof_allows_both_previously_blocked_grants()
    {
        var admin = await vault.SignInNewAccountAsync("it-sa-real-proof", IPAddress.Parse("198.18.111.24"), ClearanceLevel.TopSecret);
        var account = await ServiceAccountsApi.CreatedAsync(vault, admin.Client, "it-sa-proof-target");
        await ServiceAccountsApi.SetEnabledAsync(admin.Client, account.Id, false);
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, admin, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("198.18.111.24"));
        await AdministratorFreshnessTests.AssertStepUpAsync(await ServiceAccountsApi.IssueKeyAsync(stale.Client, account.Id), "code");
        await AdministratorFreshnessTests.AssertStepUpAsync(await ServiceAccountsApi.SetEnabledAsync(stale.Client, account.Id, true), "code");
        var owner = await ProfileHttp.AccountAsync(vault, "it-sa-real-proof");

        var proof = await UiApiRequest.WriteWithAntiforgeryAsync(stale, HttpMethod.Post, "/api/ui/step-up", JsonContent.Create(new { code = Totp.Code(owner.TotpSecret!, Totp.CurrentStep() + 1) }));

        Assert.Equal(HttpStatusCode.NoContent, proof.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ServiceAccountsApi.IssueKeyAsync(stale.Client, account.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ServiceAccountsApi.SetEnabledAsync(stale.Client, account.Id, true)).StatusCode);
        var saved = await ServiceAccountsApi.FindAsync(vault, "it-sa-proof-target");
        Assert.False(saved!.IsDisabled);
        Assert.Single(saved.Keys);
    }

    [Fact]
    public async Task An_administrator_without_an_authenticator_gets_sign_in_proof_for_both_grants()
    {
        var admin = await vault.SignInNewAccountAsync("it-sa-no-factor", IPAddress.Parse("198.18.111.25"), ClearanceLevel.TopSecret);
        using var scope = vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Users.Where(user => user.Username == "it-sa-no-factor")
            .ExecuteUpdateAsync(change => change.SetProperty(user => user.TotpEnabled, false));
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, admin, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("198.18.111.25"));
        await AdministratorFreshnessTests.AssertStepUpAsync(await ServiceAccountsApi.IssueKeyAsync(stale.Client, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")), "sign_in");
        await AdministratorFreshnessTests.AssertStepUpAsync(await ServiceAccountsApi.SetEnabledAsync(stale.Client, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), true), "sign_in");
        Assert.Equal(HttpStatusCode.Redirect, (await UiApiRequest.WriteWithAntiforgeryAsync(stale, HttpMethod.Post, "/Account/Logout")).StatusCode);
        Assert.Null(stale.SessionCookie);
        var login = await stale.Client.GetAsync("/Account/Login?ReturnUrl=%2FServiceAccounts");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("/ServiceAccounts", await login.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task New_account_metadata_lists_only_the_defined_clearances()
    {
        var admin = await vault.AdminClientAsync();
        var metadata = await admin.GetAsync(ServiceAccountsApi.Path + "/new");
        Assert.True(metadata.Headers.CacheControl?.NoStore);
        using var options = JsonDocument.Parse(await metadata.Content.ReadAsStringAsync());
        Assert.Equal(["clearances"], ProfileApi.NamesOf(options.RootElement));
        Assert.Equal("[{\"value\":1,\"label\":\"Standard\"},{\"value\":2,\"label\":\"Elevated\"},{\"value\":3,\"label\":\"Secret\"},{\"value\":4,\"label\":\"Top Secret\"}]", options.RootElement.GetProperty("clearances").GetRawText());
    }

    [Fact]
    public async Task Creation_returns_only_the_saved_service_account_id()
    {
        var admin = await vault.AdminClientAsync();
        var created = await ServiceAccountsApi.WriteAsync(admin, HttpMethod.Post, ServiceAccountsApi.Path,
            JsonContent.Create(new { name = " it-sa-shape ", clearance = 3, project = " straße " }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True(created.Headers.CacheControl?.NoStore);
        using var identity = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal(["serviceAccountId"], ProfileApi.NamesOf(identity.RootElement));
        var id = identity.RootElement.GetProperty("serviceAccountId").GetGuid();
        Assert.Equal(id, (await ServiceAccountsApi.FindAsync(vault, "it-sa-shape"))!.Id);
    }

    [Fact]
    public async Task Details_returns_saved_attributes_and_strictly_lower_ceilings()
    {
        var admin = await vault.AdminClientAsync();
        var created = await ServiceAccountsApi.WriteAsync(admin, HttpMethod.Post, ServiceAccountsApi.Path,
            JsonContent.Create(new { name = " it-sa-shape-details ", clearance = 3, project = " straße " }));
        var id = (await created.Content.ReadFromJsonAsync<CreatedServiceAccountResponse>())!.ServiceAccountId;
        var details = await admin.GetAsync($"/API/UI/SERVICE-ACCOUNTS/{id.ToString().ToUpperInvariant()}");
        Assert.True(details.Headers.CacheControl?.NoStore);
        using var saved = JsonDocument.Parse(await details.Content.ReadAsStringAsync());
        Assert.Equal(["ceilings", "clearance", "clearanceLabel", "createdAtUtc", "enabled", "keys", "name", "project", "serviceAccountId"], ProfileApi.NamesOf(saved.RootElement));
        Assert.Equal("it-sa-shape-details", saved.RootElement.GetProperty("name").GetString());
        Assert.Equal("STRAßE", saved.RootElement.GetProperty("project").GetString());
        Assert.Equal("Secret", saved.RootElement.GetProperty("clearanceLabel").GetString());
        Assert.Equal(3, saved.RootElement.GetProperty("clearance").GetInt32());
        Assert.True(saved.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Empty(saved.RootElement.GetProperty("keys").EnumerateArray());
        Assert.Equal("[{\"value\":1,\"label\":\"Standard\"},{\"value\":2,\"label\":\"Elevated\"}]", saved.RootElement.GetProperty("ceilings").GetRawText());
    }

    [Fact]
    public async Task The_list_returns_only_the_published_account_summary()
    {
        var admin = await vault.AdminClientAsync();
        var id = (await ServiceAccountsApi.CreatedAsync(vault, admin, "it-sa-shape-list")).Id;
        var listResponse = await admin.GetAsync(ServiceAccountsApi.Path);
        Assert.True(listResponse.Headers.CacheControl?.NoStore);
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.Equal(["serviceAccounts"], ProfileApi.NamesOf(list.RootElement));
        var row = Assert.Single(list.RootElement.GetProperty("serviceAccounts").EnumerateArray(), account => account.GetProperty("serviceAccountId").GetGuid() == id);
        Assert.Equal(["activeKeys", "clearance", "clearanceLabel", "createdAtUtc", "enabled", "name", "project", "serviceAccountId"], ProfileApi.NamesOf(row));
        Assert.Equal(0, row.GetProperty("activeKeys").GetInt32());
        Assert.Equal("it-sa-shape-list", row.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Issuance_returns_the_raw_key_once_without_cookie_url_log_or_audit_handoff()
    {
        var admin = await vault.SignInNewAccountAsync("it-sa-private", IPAddress.Parse("198.18.111.26"), ClearanceLevel.TopSecret);
        var account = await ServiceAccountsApi.CreatedAsync(vault, admin.Client, "it-sa-once");
        var before = await AuditTrail.HeadAsync(vault);
        var response = await ServiceAccountsApi.IssueKeyAsync(admin.Client, account.Id, JsonContent.Create(new
        { expiresInDays = 3, clearanceCeiling = 1, projectScope = " ops ", metadataOnly = true, allowedRanges = "198.18.111.26, 198.18.111.26; 10.8.0.0/24" }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["apiKey", "keyId", "prefix"], ProfileApi.NamesOf(json.RootElement));
        var key = (await response.Content.ReadFromJsonAsync<IssuedServiceAccountKeyResponse>())!;
        var hasKeyFormat = key.ApiKey.StartsWith("evk_", StringComparison.Ordinal);
        var hasReturnedPrefix = key.ApiKey.StartsWith(key.Prefix, StringComparison.Ordinal);
        Assert.True(hasKeyFormat);
        Assert.True(hasReturnedPrefix);
        Assert.False(response.Headers.ToString().Contains(key.ApiKey, StringComparison.Ordinal));
        Assert.False(admin.Cookies.GetCookieHeader(admin.Client.BaseAddress!).Contains(key.ApiKey, StringComparison.Ordinal));
        using var scope = vault.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Cookie = admin.Cookies.GetCookieHeader(admin.Client.BaseAddress!);
        var handoff = scope.ServiceProvider.GetRequiredService<ITempDataProvider>().LoadTempData(context);
        Assert.False(handoff.ContainsKey("IssuedApiKey"));
        Assert.False(JsonSerializer.Serialize(handoff).Contains(key.ApiKey, StringComparison.Ordinal));
        var read = await admin.Client.GetAsync($"{ServiceAccountsApi.Path}/{account.Id}");
        Assert.False((await read.Content.ReadAsStringAsync()).Contains(key.ApiKey, StringComparison.Ordinal));
        using var body = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        var saved = Assert.Single(body.RootElement.GetProperty("keys").EnumerateArray());
        Assert.Equal(["allowedRanges", "clearanceCeilingLabel", "createdAtUtc", "expiresAtUtc", "fullAccess", "keyId", "lastUsedAtUtc", "metadataOnly", "prefix", "projectScope", "status"], ProfileApi.NamesOf(saved));
        Assert.Equal("active", saved.GetProperty("status").GetString());
        Assert.Equal("Standard", saved.GetProperty("clearanceCeilingLabel").GetString());
        Assert.Equal("OPS", saved.GetProperty("projectScope").GetString());
        Assert.False(saved.GetProperty("fullAccess").GetBoolean());
        Assert.True(saved.GetProperty("metadataOnly").GetBoolean());
        Assert.Equal("[\"198.18.111.26/32\",\"10.8.0.0/24\"]", saved.GetProperty("allowedRanges").GetRawText());
        Assert.Equal(JsonValueKind.Null, saved.GetProperty("lastUsedAtUtc").ValueKind);
        Assert.Equal(key.KeyId, saved.GetProperty("keyId").GetGuid());
        await ProofDisclosure.AssertNotDisclosedAsync(vault, before, (await ProfileHttp.AccountAsync(vault, "it-sa-private")).Id,
            $"/api/ui/service-accounts/{account.Id}/keys", key.ApiKey);
        var publicClient = vault.CreateSession(source: IPAddress.Parse("198.18.111.26")).Client;
        publicClient.DefaultRequestHeaders.Add("X-Api-Key", key.ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await publicClient.GetAsync("/api/v1/secrets")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ServiceAccountsApi.RevokeKeyAsync(admin.Client, account.Id, key.KeyId)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await publicClient.GetAsync("/api/v1/secrets")).StatusCode);
    }

    [Fact]
    public async Task Status_and_active_counts_use_the_server_clock_and_revocation_takes_precedence()
    {
        var admin = await vault.AdminClientAsync();
        var account = await ServiceAccountsApi.CreatedAsync(vault, admin, "it-sa-statuses");
        var active = await ServiceAccountsApi.IssuedAsync(admin, account.Id);
        var expired = await ServiceAccountsApi.IssuedAsync(admin, account.Id);
        var revoked = await ServiceAccountsApi.IssuedAsync(admin, account.Id);
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        await db.ApiKeys.Where(key => key.Id == expired.KeyId || key.Id == revoked.KeyId)
            .ExecuteUpdateAsync(change => change.SetProperty(key => key.ExpiresAtUtc, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        await ServiceAccountsApi.RevokeKeyAsync(admin, account.Id, revoked.KeyId);

        var saved = await admin.GetFromJsonAsync<ServiceAccountDetailsResponse>($"{ServiceAccountsApi.Path}/{account.Id}");
        Assert.Equal("active", Assert.Single(saved!.Keys, key => key.KeyId == active.KeyId).Status);
        Assert.Equal("expired", Assert.Single(saved.Keys, key => key.KeyId == expired.KeyId).Status);
        Assert.Equal("revoked", Assert.Single(saved.Keys, key => key.KeyId == revoked.KeyId).Status);
        var list = await admin.GetFromJsonAsync<ServiceAccountsResponse>(ServiceAccountsApi.Path);
        Assert.Equal(1, Assert.Single(list!.ServiceAccounts, row => row.ServiceAccountId == account.Id).ActiveKeys);
    }

    [Theory]
    [InlineData("{\"name\":\"x\",\"clearance\":1,\"project\":\"GLOBAL\"}", "name", "Service account names are 3 to 64 characters: letters, digits, underscores, hyphens or dots.")]
    [InlineData("{\"name\":\"valid-name\",\"clearance\":7,\"project\":\"GLOBAL\"}", "clearance", "Choose one of the vault's clearance levels.")]
    [InlineData("{\"name\":\"valid-name\",\"project\":\"GLOBAL\"}", "clearance", "Choose one of the vault's clearance levels.")]
    [InlineData("{\"name\":\"valid-name\",\"clearance\":1,\"project\":\" \"}", "project", "Project keys are 1 to 64 characters, not counting spaces at either end.")]
    public async Task Invalid_create_fields_are_safe_refusals(string json, string field, string message)
    {
        var before = await AuditTrail.HeadAsync(vault);
        var response = await ServiceAccountsApi.WriteAsync(await vault.AdminClientAsync(), HttpMethod.Post, ServiceAccountsApi.Path,
            new StringContent(json, Encoding.UTF8, "application/json"));
        await UserFormsApiTests.AssertFieldProblemAsync(response, field, message);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task A_taken_name_is_a_conflict_and_records_nothing()
    {
        var admin = await vault.AdminClientAsync();
        await ServiceAccountsApi.CreatedAsync(vault, admin, "it-sa-taken");
        var before = await AuditTrail.HeadAsync(vault);
        await UiApiFailureContractTests.AssertProblemAsync(await ServiceAccountsApi.CreateAsync(admin, "it-sa-taken"), 409, "name_taken");
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Theory]
    [InlineData("{}", "expiresInDays")]
    [InlineData("{\"expiresInDays\":null}", "expiresInDays")]
    [InlineData("{\"expiresInDays\":0,\"allowedRanges\":\"invalid\"}", "allowedRanges")]
    [InlineData("{\"expiresInDays\":0,\"clearanceCeiling\":0}", "clearanceCeiling")]
    public async Task Missing_or_invalid_options_never_issue_a_key(string json, string field)
    {
        var admin = await vault.AdminClientAsync();
        var account = await ServiceAccountsApi.CreatedAsync(vault, admin, "it-sa-invalid-" + Guid.NewGuid().ToString("N"));
        var before = await AuditTrail.HeadAsync(vault);
        var response = await ServiceAccountsApi.IssueKeyAsync(admin, account.Id, new StringContent(json, Encoding.UTF8, "application/json"));
        await ServiceAccountsApi.AssertValidationAsync(response);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal([field], ProfileApi.NamesOf(problem.RootElement.GetProperty("errors")));
        Assert.Empty((await ServiceAccountsApi.FindAsync(vault, account.Name))!.Keys);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Theory]
    [InlineData("/ServiceAccounts", "198.18.111.31")]
    [InlineData("/sErViCeAcCoUnTs/InDeX", "198.18.111.32")]
    [InlineData("/SERVICEACCOUNTS/CREATE", "198.18.111.33")]
    [InlineData("/sErViCeAcCoUnTs/DeTaIlS/AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA", "198.18.111.34")]
    public async Task Bookmarks_keep_admin_authorization_return_urls_and_the_Angular_document(string path, string source)
    {
        var anonymous = await vault.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Equal(path, System.Web.HttpUtility.ParseQueryString(anonymous.Headers.Location!.Query)["ReturnUrl"]);
        var reader = await vault.SignInNewAccountAsync("it-sa-page-reader-" + source.Split('.').Last(), IPAddress.Parse(source));
        var denied = await reader.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/Denied", denied.Headers.Location?.AbsolutePath);
        var admin = await vault.AdminClientAsync();
        var document = await admin.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.True(document.Headers.CacheControl?.NoStore);
        Assert.Contains("<app-root>", await document.Content.ReadAsStringAsync());
        Assert.Contains("require-trusted-types-for 'script'", Assert.Single(document.Headers.GetValues("Content-Security-Policy")));
        var post = await ServiceAccountsApi.WriteAsync(admin, HttpMethod.Post, path, JsonContent.Create(new { }));
        Assert.True(post.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
    }
}
