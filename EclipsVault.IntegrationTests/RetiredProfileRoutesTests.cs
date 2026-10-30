using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class RetiredProfileRoutesTests(VaultFixture vault)
{
    [Theory]
    [InlineData("/Profile/Update", HttpStatusCode.NotFound, "100.91.1.1")]
    [InlineData("/pRoFiLe/uPdAtE/", HttpStatusCode.NotFound, "100.91.1.2")]
    [InlineData("/Profile/Update/11111111-1111-1111-1111-111111111111", HttpStatusCode.NotFound, "100.91.1.3")]
    [InlineData("/Profile/UploadAvatar", HttpStatusCode.NotFound, "100.91.1.4")]
    [InlineData("/Profile/RemoveAvatar", HttpStatusCode.NotFound, "100.91.1.5")]
    [InlineData("/Profile/ChangePassword", HttpStatusCode.MethodNotAllowed, "100.91.1.6")]
    [InlineData("/pRoFiLe/cHaNgEpAsSwOrD/legacy", HttpStatusCode.NotFound, "100.91.1.7")]
    [InlineData("/Profile/ResetMfa", HttpStatusCode.NotFound, "100.91.1.8")]
    [InlineData("/Profile/GenerateRecoveryCodes", HttpStatusCode.NotFound, "100.91.1.9")]
    [InlineData("/Profile/SignOutEverywhere", HttpStatusCode.NotFound, "100.91.1.10")]
    [InlineData("/Profile/PasskeyRegisterBegin", HttpStatusCode.NotFound, "100.91.1.11")]
    [InlineData("/Profile/PasskeyRegisterComplete", HttpStatusCode.NotFound, "100.91.1.12")]
    [InlineData("/Profile/RemovePasskey", HttpStatusCode.NotFound, "100.91.1.13")]
    [InlineData("/Profile/RemovePasskey/11111111-1111-1111-1111-111111111111", HttpStatusCode.NotFound, "100.91.1.14")]
    [InlineData("/Profile/CheckPasswordBreached", HttpStatusCode.NotFound, "100.91.1.15")]
    public async Task A_retired_Profile_write_has_no_handler_with_a_real_session_and_valid_antiforgery(
        string path, HttpStatusCode expected, string source)
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-retired-" + Guid.NewGuid().ToString("N"), source);
        var before = await AuditTrail.HeadAsync(vault);
        var response = await UiApiRequest.WriteWithAntiforgeryAsync(owner.Browser, HttpMethod.Post, path,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["DisplayName"] = "Unverified edit",
                ["Email"] = "unverified@example.test",
                ["CurrentPassword"] = owner.Password,
                ["NewPassword"] = "Unverified-New-Password-91!",
                ["ConfirmPassword"] = "Unverified-New-Password-91!",
                ["id"] = owner.Id.ToString(),
            }));

        Assert.Equal(expected, response.StatusCode);
        var stored = await ProfileHttp.AccountAsync(vault, owner.Stored.Username);
        Assert.Equal(owner.Stored.Email, stored.Email);
        Assert.Equal(owner.Stored.DisplayName, stored.DisplayName);
        Assert.Equal(owner.Stored.PasswordHash, stored.PasswordHash);
        Assert.Equal(owner.Stored.TotpSecret, stored.TotpSecret);
        Assert.Equal(owner.Stored.CredentialGeneration, stored.CredentialGeneration);
        Assert.Empty(await ProfileHttp.EventsAfterAsync(vault, before, owner.Id));
    }

    [Theory]
    [InlineData("PUT", "/pRoFiLe/UpDaTe", HttpStatusCode.NotFound, "100.91.2.1")]
    [InlineData("PATCH", "/Profile/Update", HttpStatusCode.NotFound, "100.91.2.2")]
    [InlineData("DELETE", "/PROFILE/REMOVEAVATAR", HttpStatusCode.NotFound, "100.91.2.3")]
    [InlineData("PUT", "/profile/changepassword", HttpStatusCode.MethodNotAllowed, "100.91.2.4")]
    [InlineData("DELETE", "/Profile/RemovePasskey/11111111-1111-1111-1111-111111111111", HttpStatusCode.NotFound, "100.91.2.5")]
    [InlineData("POST", "/pRoFiLe/AvAtAr", HttpStatusCode.MethodNotAllowed, "100.91.2.6")]
    public async Task No_other_method_or_case_reaches_a_retired_Profile_write(
        string method, string path, HttpStatusCode expected, string source)
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-retired-verb-" + Guid.NewGuid().ToString("N"), source);
        var before = await AuditTrail.HeadAsync(vault);
        var response = await UiApiRequest.WriteWithAntiforgeryAsync(owner.Browser, new HttpMethod(method), path,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["DisplayName"] = "Unverified edit",
                ["Email"] = "unverified@example.test",
                ["NewPassword"] = "Unverified-New-Password-91!",
            }));

        Assert.Equal(expected, response.StatusCode);
        var stored = await ProfileHttp.AccountAsync(vault, owner.Stored.Username);
        Assert.Equal(owner.Stored.Email, stored.Email);
        Assert.Equal(owner.Stored.DisplayName, stored.DisplayName);
        Assert.Equal(owner.Stored.PasswordHash, stored.PasswordHash);
        Assert.Equal(owner.Stored.CredentialGeneration, stored.CredentialGeneration);
        Assert.Empty(await ProfileHttp.EventsAfterAsync(vault, before, owner.Id));
    }

    [Fact]
    public async Task The_shared_avatar_is_session_only_and_never_cached()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-avatar-boundary", "100.91.0.2");
        var avatar = await owner.Browser.Client.GetAsync("/Profile/Avatar?seed=Legacy%20Users");
        Assert.Equal(HttpStatusCode.OK, avatar.StatusCode);
        Assert.True(avatar.Headers.CacheControl?.NoStore);
        Assert.Equal("image/svg+xml", avatar.Content.Headers.ContentType?.MediaType);

        var anonymous = await vault.CreateClient().GetAsync("/Profile/Avatar");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Equal("/Account/Login", anonymous.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Another_session_reads_an_accounts_uploaded_avatar_but_an_api_key_cannot()
    {
        var owner = await ProfileAccount.CreateAsync(vault, "it-avatar-shared-owner", "100.91.0.3");
        var reader = await ProfileAccount.CreateAsync(vault, "it-avatar-shared-reader", "100.91.0.4");
        Assert.Equal(HttpStatusCode.NoContent, (await ProfileHttp.UploadAvatarAsync(owner.Browser, ProfileHttp.TinyPng)).StatusCode);

        var shared = await reader.Browser.Client.GetAsync($"/Profile/Avatar?id={owner.Id}");
        var own = await reader.Browser.Client.GetAsync("/Profile/Avatar");

        Assert.Equal(HttpStatusCode.OK, shared.StatusCode);
        Assert.Equal("image/png", shared.Content.Headers.ContentType?.MediaType);
        Assert.True(shared.Headers.CacheControl?.NoStore);
        Assert.Equal("image/svg+xml", own.Content.Headers.ContentType?.MediaType);

        var service = vault.CreateClient();
        service.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), "it-avatar-refuses-api-key"));
        var byKey = await service.GetAsync($"/Profile/Avatar?id={owner.Id}");
        Assert.NotEqual(HttpStatusCode.OK, byKey.StatusCode);
        Assert.NotEqual("image/png", byKey.Content.Headers.ContentType?.MediaType);
    }
}
