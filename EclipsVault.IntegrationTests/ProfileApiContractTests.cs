using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// What every Profile UI API route promises before any account verification: the session alone,
/// never an API key; antiforgery on every write; no-store; Web-chosen shapes; and only the session's
/// own account, whatever the request body or query names.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class ProfileApiContractTests(VaultFixture vault)
{
    private const string AnyId = "11111111-1111-1111-1111-111111111111";

    [Theory]
    [InlineData("GET", "/api/ui/profile")]
    [InlineData("PATCH", "/api/ui/profile/details")]
    [InlineData("POST", "/api/ui/profile/avatar")]
    [InlineData("DELETE", "/api/ui/profile/avatar")]
    [InlineData("POST", "/api/ui/profile/sign-out-everywhere")]
    [InlineData("POST", "/api/ui/passwords/check-breached")]
    [InlineData("POST", "/api/ui/profile/operations")]
    [InlineData("DELETE", "/api/ui/profile/operations/" + AnyId)]
    [InlineData("POST", "/api/ui/profile/operations/" + AnyId + "/verify")]
    [InlineData("POST", "/api/ui/profile/operations/" + AnyId + "/passkey/begin")]
    [InlineData("POST", "/api/ui/profile/operations/" + AnyId + "/passkey/complete")]
    [InlineData("POST", "/api/ui/profile/password")]
    [InlineData("POST", "/api/ui/profile/authenticator/reset")]
    [InlineData("POST", "/api/ui/profile/recovery-codes")]
    [InlineData("POST", "/api/ui/profile/passkeys/registration/begin")]
    [InlineData("POST", "/api/ui/profile/passkeys/registration/complete")]
    [InlineData("DELETE", "/api/ui/profile/passkeys/" + AnyId)]
    [InlineData("POST", "/api/ui/profile/email/request")]
    [InlineData("POST", "/api/ui/profile/email/resend")]
    [InlineData("POST", "/api/ui/profile/email/confirm")]
    [InlineData("DELETE", "/api/ui/profile/email/pending")]
    public async Task Each_profile_route_refuses_anonymous_and_working_api_key_callers(string method, string path)
    {
        var client = vault.CreateClient();
        await UiApiFailureContractTests.AssertProblemAsync(await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)), 401, "unauthenticated");
        var key = await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), $"it-ui87-profile-key-{method}-{path.GetHashCode():x}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/secrets")).StatusCode);

        await UiApiFailureContractTests.AssertProblemAsync(await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)), 401, "unauthenticated");
    }

    [Theory]
    [InlineData("POST", "/api/ui/passwords/check-breached", "100.64.89.31")]
    [InlineData("PATCH", "/api/ui/profile/details", "100.64.87.1")]
    [InlineData("DELETE", "/api/ui/profile/avatar", "100.64.87.3")]
    [InlineData("POST", "/api/ui/profile/sign-out-everywhere", "100.64.87.4")]
    [InlineData("POST", "/api/ui/profile/operations", "100.64.87.5")]
    [InlineData("DELETE", "/api/ui/profile/operations/" + AnyId, "100.64.87.6")]
    [InlineData("POST", "/api/ui/profile/operations/" + AnyId + "/verify", "100.64.87.7")]
    [InlineData("POST", "/api/ui/profile/operations/" + AnyId + "/passkey/begin", "100.64.87.8")]
    [InlineData("POST", "/api/ui/profile/operations/" + AnyId + "/passkey/complete", "100.64.87.9")]
    [InlineData("POST", "/api/ui/profile/password", "100.64.87.10")]
    [InlineData("POST", "/api/ui/profile/authenticator/reset", "100.64.87.11")]
    [InlineData("POST", "/api/ui/profile/recovery-codes", "100.64.87.12")]
    [InlineData("POST", "/api/ui/profile/passkeys/registration/begin", "100.64.87.13")]
    [InlineData("POST", "/api/ui/profile/passkeys/registration/complete", "100.64.87.14")]
    [InlineData("DELETE", "/api/ui/profile/passkeys/" + AnyId, "100.64.87.15")]
    [InlineData("POST", "/api/ui/profile/email/request", "100.64.88.21")]
    [InlineData("POST", "/api/ui/profile/email/resend", "100.64.88.22")]
    [InlineData("POST", "/api/ui/profile/email/confirm", "100.64.88.23")]
    [InlineData("DELETE", "/api/ui/profile/email/pending", "100.64.88.24")]
    public async Task Each_profile_write_refuses_a_session_without_its_antiforgery_token(string method, string path, string address)
    {
        var owner = await ProfileAccount.CreateAsync(vault, $"it-ui87-profile-csrf-{address.Split('.')[^1]}", address);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await owner.Browser.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { operationId = AnyId }),
        });

        await UiApiFailureContractTests.AssertProblemAsync(response, 400, "antiforgery_rejected");
        Assert.Empty(await ProfileHttp.EventsAfterAsync(vault, before, owner.Id));
        Assert.Equal(HttpStatusCode.OK, (await owner.Browser.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task Avatar_upload_refuses_a_session_without_its_antiforgery_token()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-profile-avatar-csrf", "100.64.87.2");
        using var image = new MultipartFormDataContent { { new ByteArrayContent(ProfileHttp.TinyPng), "avatar", "avatar.png" } };

        await UiApiFailureContractTests.AssertProblemAsync(await owner.Browser.Client.PostAsync("/api/ui/profile/avatar", image), 400, "antiforgery_rejected");

        Assert.False(await ProfileHttp.HasAvatarAsync(vault, owner.Id));
    }

    [Fact]
    public async Task The_profile_page_data_names_display_and_capability_fields_only()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-profile-snapshot", "100.64.87.16");
        using var authenticator = new PasskeyAuthenticator();
        var passkeyId = await ProfileApi.AddPasskeyAsync(owner, authenticator, "Laptop key");

        var response = await owner.Browser.Client.GetAsync("/api/ui/profile");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var profile = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            ["authenticatorEnrolled", "avatarUrl", "changes", "clearance", "displayName", "email", "hasCustomAvatar", "passkeys", "pendingEmail", "project",
                "recoveryCodesRemaining", "username", "verificationMethods"],
            ProfileApi.NamesOf(profile));
        Assert.Equal("it-ui87-profile-snapshot", profile.GetProperty("username").GetString());
        Assert.Equal("Integration Test", profile.GetProperty("displayName").GetString());
        Assert.Equal(owner.Stored.Email, profile.GetProperty("email").GetString());
        Assert.Equal("Standard", profile.GetProperty("clearance").GetString());
        Assert.Equal("GLOBAL", profile.GetProperty("project").GetString());
        Assert.StartsWith("/Profile/Avatar?v=", profile.GetProperty("avatarUrl").GetString());
        Assert.False(profile.GetProperty("hasCustomAvatar").GetBoolean());
        Assert.True(profile.GetProperty("authenticatorEnrolled").GetBoolean());
        Assert.Equal(0, profile.GetProperty("recoveryCodesRemaining").GetInt32());
        Assert.Equal(["PasswordAndAuthenticatorCode", "Passkey"],
            profile.GetProperty("verificationMethods").EnumerateArray().Select(method => method.GetString()));
        var passkey = Assert.Single(profile.GetProperty("passkeys").EnumerateArray().ToArray());
        Assert.Equal(["createdAtUtc", "id", "nickname", "removal"], ProfileApi.NamesOf(passkey));
        Assert.Equal(passkeyId, passkey.GetProperty("id").GetGuid());
        Assert.Equal("Laptop key", passkey.GetProperty("nickname").GetString());
        Assert.EndsWith("Z", passkey.GetProperty("createdAtUtc").GetString());
        Assert.Equal("""{"available":true,"unavailableReason":null}""", passkey.GetProperty("removal").GetRawText());
        Assert.Equal(
            """{"changePassword":{"available":true,"unavailableReason":null},"resetAuthenticator":{"available":true,"unavailableReason":null},"replaceRecoveryCodes":{"available":true,"unavailableReason":null},"addPasskey":{"available":true,"unavailableReason":null},"changeEmail":{"available":false,"unavailableReason":"mail_unavailable","explanation":"Email changes require configured, enabled SMTP delivery."}}""",
            profile.GetProperty("changes").GetRawText());
    }

    [Fact]
    public async Task Saving_the_display_name_renews_this_session_and_records_one_profile_event()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-profile-display-name", "100.64.87.17");
        var sessionCookie = owner.Browser.SessionCookie!.Value;
        var before = await AuditTrail.HeadAsync(vault);

        var response = await ProfileApi.SendAsync(owner.Browser, HttpMethod.Patch, "/api/ui/profile/details", new { displayName = "  Renamed Person  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("""{"displayName":"Renamed Person"}""", await response.Content.ReadAsStringAsync());
        Assert.Equal("Renamed Person", (await owner.Browser.Client.GetFromJsonAsync<JsonElement>("/api/ui/me")).GetProperty("displayName").GetString());
        Assert.Equal([AuditAction.ProfileUpdated], await ProfileHttp.EventsAfterAsync(vault, before, owner.Id));
        Assert.Equal(HttpStatusCode.OK, (await vault.CreateSession(new System.Net.Cookie(VaultFixture.SessionCookie, sessionCookie)).Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task Saving_details_refuses_an_email_or_account_field_without_saving_anything()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-profile-details-overpost", "100.64.87.18");
        var other = await ProfileAccount.CreateAsync(vault, "it-ui87-profile-details-overpost-b", "100.64.87.19");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await ProfileApi.SendAsync(owner.Browser, HttpMethod.Patch, "/api/ui/profile/details",
            new { displayName = "Overposted", email = "taken@example.test", accountId = other.Id });

        var problem = await ProfileApi.AssertProblemAsync(response, 400, "validation_failed");
        Assert.DoesNotContain("taken@example.test", problem.GetRawText());
        var stored = await ProfileHttp.AccountAsync(vault, "it-ui87-profile-details-overpost");
        Assert.Equal("Integration Test", stored.DisplayName);
        Assert.Equal(owner.Stored.Email, stored.Email);
        Assert.Equal("Integration Test", (await ProfileHttp.AccountAsync(vault, "it-ui87-profile-details-overpost-b")).DisplayName);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task Uploading_the_avatar_records_one_event_and_answers_no_content()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-profile-avatar", "100.64.87.20");
        var before = await AuditTrail.HeadAsync(vault);
        using var image = new MultipartFormDataContent { { new ByteArrayContent(ProfileHttp.TinyPng), "avatar", "avatar.png" } };

        var uploaded = await UiApiRequest.WriteWithAntiforgeryAsync(owner.Browser, HttpMethod.Post, "/api/ui/profile/avatar", image);

        Assert.Equal(HttpStatusCode.NoContent, uploaded.StatusCode);
        Assert.True(uploaded.Headers.CacheControl?.NoStore);
        Assert.True(await ProfileHttp.HasAvatarAsync(vault, owner.Id));
        Assert.True((await owner.Browser.Client.GetFromJsonAsync<JsonElement>("/api/ui/profile")).GetProperty("hasCustomAvatar").GetBoolean());
        Assert.Equal([AuditAction.AvatarUpdated], await ProfileHttp.EventsAfterAsync(vault, before, owner.Id));
    }

    [Fact]
    public async Task Removing_the_avatar_records_one_event_and_answers_no_content()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-profile-avatar-remove", "100.64.87.27");
        await ProfileHttp.UploadAvatarAsync(owner.Browser, ProfileHttp.TinyPng);
        var before = await AuditTrail.HeadAsync(vault);

        var removed = await ProfileApi.SendAsync(owner.Browser, HttpMethod.Delete, "/api/ui/profile/avatar");

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.True(removed.Headers.CacheControl?.NoStore);
        Assert.False(await ProfileHttp.HasAvatarAsync(vault, owner.Id));
        Assert.Equal([AuditAction.AvatarRemoved], await ProfileHttp.EventsAfterAsync(vault, before, owner.Id));
    }

    [Fact]
    public async Task An_avatar_that_is_not_an_image_is_refused_on_the_page()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-profile-avatar-invalid", "100.64.87.21");
        using var notImage = new MultipartFormDataContent { { new ByteArrayContent("not an image"u8.ToArray()), "avatar", "avatar.png" } };

        var response = await UiApiRequest.WriteWithAntiforgeryAsync(owner.Browser, HttpMethod.Post, "/api/ui/profile/avatar", notImage);

        await ProfileApi.AssertProblemAsync(response, 400, "validation_failed");
        Assert.False(await ProfileHttp.HasAvatarAsync(vault, owner.Id));
    }

    [Fact]
    public async Task Signing_out_everywhere_ends_every_session_records_it_and_leaves_the_message_for_sign_in()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-profile-sign-out", "100.64.87.22");
        var copied = vault.CreateSession(owner.Browser.SessionCookie, IPAddress.Parse("100.64.87.23"));
        var before = await AuditTrail.HeadAsync(vault);

        var response = await ProfileApi.SendAsync(owner.Browser, HttpMethod.Post, "/api/ui/profile/sign-out-everywhere");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(owner.Browser.SessionCookie);
        await UiApiFailureContractTests.AssertProblemAsync(await copied.Client.GetAsync("/api/ui/me"), 401, "unauthenticated");
        Assert.Equal([AuditAction.SessionsRevokedSelf], await ProfileHttp.EventsAfterAsync(vault, before, owner.Id));
        Assert.Contains("You have been signed out of all sessions.", WebUtility.HtmlDecode(await owner.Browser.Client.GetStringAsync("/Account/Login")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_administrator_reads_and_prepares_only_for_their_own_account()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-profile-admin-scope", "100.64.87.25");
        var ownerOperation = await ProfileApi.PreparedAsync(owner.Browser, new { kind = "ResetAuthenticator" });
        var adminClient = await vault.AdminClientAsync();

        var adminProfile = await adminClient.GetFromJsonAsync<JsonElement>($"/api/ui/profile?userId={owner.Id}&accountId={owner.Id}");
        using var cancelRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/ui/profile/operations/{ownerOperation}");
        cancelRequest.Headers.Add("RequestVerificationToken", Microsoft.Net.Http.Headers.SetCookieHeaderValue.ParseList((await adminClient.GetAsync("/api/ui/me")).Headers.GetValues("Set-Cookie").ToList()).Single(cookie => cookie.Name == "XSRF-TOKEN").Value.ToString());
        var cancel = await adminClient.SendAsync(cancelRequest);

        Assert.Equal(VaultFixture.AdminUsername, adminProfile.GetProperty("username").GetString());
        await ProfileApi.AssertProblemAsync(cancel, 404, "operation_not_found");
        Assert.Equal(1, await OperationsOfAsync(owner.Id));
    }

    [Fact]
    public async Task A_profile_route_does_not_answer_a_method_it_does_not_declare()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-ui87-profile-methods", "100.64.87.26");

        var response = await owner.Browser.Client.GetAsync("/api/ui/profile/password");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<int> OperationsOfAsync(Guid accountId)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        return await db.ProfileOperations.CountAsync(operation => operation.UserId == accountId);
    }
}
