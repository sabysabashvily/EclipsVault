using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Models.Ui;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class UserFormsApiTests(VaultFixture vault)
{
    private const string Missing = "/api/ui/users/11111111-1111-1111-1111-111111111111";

    [Theory]
    [InlineData("GET", "/api/ui/users/new", "198.18.110.11")]
    [InlineData("POST", "/api/ui/users", "198.18.110.12")]
    [InlineData("GET", Missing, "198.18.110.13")]
    [InlineData("PATCH", Missing + "/role", "198.18.110.14")]
    public async Task Form_endpoints_accept_only_an_administrator_session(string method, string path, string source)
    {
        var anonymous = vault.CreateClient();
        var reader = await vault.SignInNewAccountAsync("it-forms-reader-" + source.Split('.').Last(), IPAddress.Parse(source));
        var apiKey = vault.CreateClient();
        apiKey.DefaultRequestHeaders.Authorization = new("Bearer", await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), "it-forms-key-" + source.Split('.').Last()));

        await UiApiFailureContractTests.AssertProblemAsync(await anonymous.SendAsync(new(new(method), path)), 401, "unauthenticated");
        await UiApiFailureContractTests.AssertProblemAsync(await reader.Client.SendAsync(new(new(method), path)), 403, "forbidden");
        await UiApiFailureContractTests.AssertProblemAsync(await apiKey.SendAsync(new(new(method), path)), 401, "unauthenticated");
    }

    [Theory]
    [InlineData("POST", "/api/ui/users")]
    [InlineData("PATCH", Missing + "/role")]
    public async Task A_form_write_without_antiforgery_is_refused(string method, string path)
    {
        var admin = await vault.AdminClientAsync();
        await UiApiFailureContractTests.AssertProblemAsync(await admin.SendAsync(new(new(method), path) { Content = JsonContent.Create(new { clearance = 1, project = "GLOBAL" }) }), 400, "antiforgery_rejected");
    }

    [Fact]
    public async Task New_user_metadata_has_only_the_configured_domain_and_defined_clearances()
    {
        var admin = await vault.AdminClientAsync();
        var response = await admin.GetAsync("/api/ui/users/new");
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["clearances", "emailDomain"], ProfileApi.NamesOf(body.RootElement));
        Assert.Equal("eclipsvault.local", body.RootElement.GetProperty("emailDomain").GetString());
        Assert.Equal("[{\"value\":1,\"label\":\"Standard\"},{\"value\":2,\"label\":\"Elevated\"},{\"value\":3,\"label\":\"Secret\"},{\"value\":4,\"label\":\"Top Secret\"}]", body.RootElement.GetProperty("clearances").GetRawText());
    }

    [Fact]
    public async Task Creating_an_account_returns_only_its_saved_identity_and_details()
    {
        var admin = await vault.AdminClientAsync();
        var before = await AuditTrail.HeadAsync(vault);
        var response = await UserAccounts.CreateAsync(admin, " it-form-created ", " Ada ", " Saved ", password: "Initial-nonreused-credential-110", project: " ops ");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["email", "userId", "username"], ProfileApi.NamesOf(body.RootElement));
        var saved = await ProfileHttp.AccountAsync(vault, "it-form-created");
        Assert.Equal(saved.Id, body.RootElement.GetProperty("userId").GetGuid());
        Assert.Equal("it-form-created", body.RootElement.GetProperty("username").GetString());
        Assert.Equal("ada.saved.1@eclipsvault.local", body.RootElement.GetProperty("email").GetString());
        var details = await admin.GetAsync($"/API/UI/USERS/{saved.Id.ToString().ToUpperInvariant()}");
        Assert.True(details.Headers.CacheControl?.NoStore);
        using var detailBody = JsonDocument.Parse(await details.Content.ReadAsStringAsync());
        Assert.Equal(["clearance", "clearances", "displayName", "isYou", "project", "userId", "username"], ProfileApi.NamesOf(detailBody.RootElement));
        Assert.Equal("Ada Saved", detailBody.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("OPS", detailBody.RootElement.GetProperty("project").GetString());
        Assert.False(detailBody.RootElement.GetProperty("isYou").GetBoolean());
        await ProofDisclosure.AssertNotDisclosedAsync(vault, before, saved.Id, "/api/ui/users", "Initial-nonreused-credential-110");
        Assert.Equal(1, detailBody.RootElement.GetProperty("clearance").GetInt32());
    }

    [Fact]
    public async Task A_taken_username_is_a_conflict_without_an_account_or_audit_change()
    {
        var admin = await vault.AdminClientAsync();
        (await UserAccounts.CreateAsync(admin, "it-form-taken")).EnsureSuccessStatusCode();
        var before = await AuditTrail.HeadAsync(vault);
        var response = await UserAccounts.CreateAsync(admin, "it-form-taken");
        await UiApiFailureContractTests.AssertProblemAsync(response, 409, "username_taken");
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        Assert.Equal("Integration Test", (await ProfileHttp.AccountAsync(vault, "it-form-taken")).DisplayName);
    }

    [Theory]
    [InlineData("firstName", "", "First name is required.")]
    [InlineData("lastName", "", "Last name is required.")]
    [InlineData("password", "000000000000", "That password has appeared in a known data breach. Choose a different one.")]
    [InlineData("project", " ", "Project keys are 1 to 64 characters, not counting spaces at either end.")]
    public async Task Invalid_account_fields_have_safe_field_messages_and_change_nothing(string field, string value, string message)
    {
        var admin = await vault.AdminClientAsync();
        var request = new Dictionary<string, object> { ["username"] = "it-form-invalid-" + field, ["firstName"] = "Valid", ["lastName"] = "Person", ["password"] = "Initial-only-unc0mmon-2026", ["clearance"] = 1, ["project"] = "GLOBAL" };
        request[field] = value;
        var before = await AuditTrail.HeadAsync(vault);
        var response = await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin), HttpMethod.Post, "/api/ui/users", JsonContent.Create(request));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_failed", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(message, body.RootElement.GetProperty("errors").GetProperty(field)[0].GetString());
        Assert.DoesNotContain("Initial-only-unc0mmon-2026", await response.Content.ReadAsStringAsync());
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task An_unchanged_own_role_keeps_the_cookie_generation_and_audit_trail()
    {
        var admin = await vault.SignInNewAccountAsync("it-form-own-noop", IPAddress.Parse("198.18.110.21"), ClearanceLevel.TopSecret);
        var account = await ProfileHttp.AccountAsync(vault, "it-form-own-noop");
        var cookie = admin.SessionCookie!.Value;
        var before = await AuditTrail.HeadAsync(vault);
        var response = await UserAccounts.EditRoleAsync(admin.Client, account.Id, ClearanceLevel.TopSecret, " global ");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(cookie, admin.SessionCookie!.Value);
        Assert.Equal(account.CredentialGeneration, (await ProfileHttp.AccountAsync(vault, "it-form-own-noop")).CredentialGeneration);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        var notice = await admin.Client.GetFromJsonAsync<UserNoticeResponse>("/api/ui/users/notice");
        Assert.Equal(new UserNoticeResponse("Role updated for 'it-form-own-noop'. Their sessions were revoked so the change takes effect at next sign-in.", false), notice);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task An_unchanged_own_Unicode_project_preserves_the_session_and_saved_notice()
    {
        var admin = await vault.SignInNewAccountAsync("it-form-own-unicode", IPAddress.Parse("198.18.110.24"), ClearanceLevel.TopSecret, project: "STRAßE");
        var account = await ProfileHttp.AccountAsync(vault, "it-form-own-unicode");
        Assert.Equal("STRAßE", account.ProjectKey);
        var cookie = admin.SessionCookie!.Value;
        var before = await AuditTrail.HeadAsync(vault);
        var response = await UserAccounts.EditRoleAsync(admin.Client, account.Id, ClearanceLevel.TopSecret, "STRAßE");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(cookie, admin.SessionCookie!.Value);
        Assert.Equal(0L, (await ProfileHttp.AccountAsync(vault, "it-form-own-unicode")).CredentialGeneration);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync("/Admin/Users")).StatusCode);
        Assert.Equal(new UserNoticeResponse("Role updated for 'it-form-own-unicode'. Their sessions were revoked so the change takes effect at next sign-in.", false),
            await admin.Client.GetFromJsonAsync<UserNoticeResponse>("/api/ui/users/notice"));
    }

    [Fact]
    public async Task Self_demotion_clears_the_cookie_and_refuses_an_old_copy()
    {
        var admin = await vault.SignInNewAccountAsync("it-form-own-demote", IPAddress.Parse("198.18.110.22"), ClearanceLevel.TopSecret);
        var oldBrowser = vault.CreateSession(admin.SessionCookie, IPAddress.Parse("198.18.110.23"));
        var account = await ProfileHttp.AccountAsync(vault, "it-form-own-demote");
        var response = await UserAccounts.EditRoleAsync(admin.Client, account.Id, ClearanceLevel.Secret, "GLOBAL");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(admin.SessionCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldBrowser.Client.GetAsync("/api/ui/me")).StatusCode);
        var stored = await ProfileHttp.AccountAsync(vault, "it-form-own-demote");
        Assert.Equal((ClearanceLevel.Secret, 1L), (stored.Clearance, stored.CredentialGeneration));
    }

    [Theory]
    [InlineData("POST", "/api/ui/users", "198.18.110.31")]
    [InlineData("PATCH", Missing + "/role", "198.18.110.32")]
    public async Task Stale_grants_check_freshness_after_metadata_and_before_lookup(string method, string path, string source)
    {
        var admin = await vault.SignInNewAccountAsync("it-form-stale-" + method, IPAddress.Parse(source), ClearanceLevel.TopSecret);
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, admin, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse(source));
        Assert.Equal(HttpStatusCode.OK, (await stale.Client.GetAsync("/api/ui/users/new")).StatusCode);
        var response = await UiApiRequest.WriteWithAntiforgeryAsync(stale, new(method), path, JsonContent.Create(new { clearance = 1, project = "GLOBAL" }));
        await AdministratorFreshnessTests.AssertStepUpAsync(response, "code");
    }

    [Fact]
    public async Task Real_step_up_allows_the_previously_stale_administrator_to_create_and_change_a_role()
    {
        var admin = await vault.SignInNewAccountAsync("it-form-proof", IPAddress.Parse("198.18.110.33"), ClearanceLevel.TopSecret);
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, admin, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("198.18.110.33"));
        await AdministratorFreshnessTests.AssertStepUpAsync(await UserAccounts.CreateAsync(stale.Client, "it-form-proof-target"), "code");
        var account = await ProfileHttp.AccountAsync(vault, "it-form-proof");
        var proof = await UiApiRequest.WriteWithAntiforgeryAsync(stale, HttpMethod.Post, "/api/ui/step-up", JsonContent.Create(new { code = Totp.Code(account.TotpSecret!, Totp.CurrentStep() + 1) }));
        Assert.Equal(HttpStatusCode.NoContent, proof.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await UserAccounts.CreateAsync(stale.Client, "it-form-proof-target")).StatusCode);
        var target = await ProfileHttp.AccountAsync(vault, "it-form-proof-target");
        Assert.Equal(HttpStatusCode.NoContent, (await UserAccounts.EditRoleAsync(stale.Client, target.Id, ClearanceLevel.Elevated, " ops ")).StatusCode);
        var stored = await ProfileHttp.AccountAsync(vault, "it-form-proof-target");
        Assert.Equal((ClearanceLevel.Elevated, "OPS", 1L), (stored.Clearance, stored.ProjectKey, stored.CredentialGeneration));
    }

    [Theory]
    [InlineData("/Admin/CreateUser")]
    [InlineData("/aDmIn/cReAtEuSeR")]
    [InlineData("/Admin/EditUser/11111111-1111-1111-1111-111111111111")]
    [InlineData("/ADMIN/EDITUSER/11111111-1111-1111-1111-111111111111")]
    public async Task Form_bookmarks_serve_the_protected_Angular_document(string path)
    {
        var anonymous = await vault.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/Account/Login", anonymous.Headers.Location?.AbsolutePath);
        var admin = await vault.AdminClientAsync();
        var document = await admin.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.True(document.Headers.CacheControl?.NoStore);
        Assert.Contains("<app-root>", await document.Content.ReadAsStringAsync());
        Assert.Contains("require-trusted-types-for 'script'", document.Headers.GetValues("Content-Security-Policy").Single());
        var retiredPost = await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin), HttpMethod.Post, path, JsonContent.Create(new { }));
        Assert.True(retiredPost.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Missing_clearance_is_a_field_refusal_and_never_a_default_role()
    {
        var admin = await vault.AdminClientAsync();
        var response = await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin), HttpMethod.Post,
            "/api/ui/users", JsonContent.Create(new { username = "it-form-no-clearance", firstName = "Missing", lastName = "Clearance", password = "Initial-nonreused-credential-110", project = "GLOBAL" }));
        await AssertFieldProblemAsync(response, "clearance", "Choose one of the vault's clearance levels.");
    }

    internal static async Task AssertFieldProblemAsync(HttpResponseMessage response, string field, string message)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["code", "errors", "status", "title"], ProfileApi.NamesOf(body.RootElement));
        Assert.Equal("validation_failed", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(400, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal([field], ProfileApi.NamesOf(body.RootElement.GetProperty("errors")));
        Assert.Equal(message, body.RootElement.GetProperty("errors").GetProperty(field)[0].GetString());
    }

    [Theory]
    [InlineData("GET", Missing)]
    [InlineData("PATCH", Missing + "/role")]
    public async Task Unknown_ids_return_user_not_found(string method, string path)
    {
        var admin = await vault.AdminClientAsync();
        var response = await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin), new(method), path,
            JsonContent.Create(new { clearance = 1, project = "GLOBAL" }));
        await UiApiFailureContractTests.AssertProblemAsync(response, 404, "user_not_found");
    }
}
