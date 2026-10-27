using System.Net;
using System.Net.Http.Json;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models.Ui;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class CredentialGenerationTests(VaultFixture vault)
{
    private const string Password = "It-generation-password-2026!";
    private const string ReplacementPassword = "It-replacement-password-2026!";

    [Fact]
    public async Task A_named_key_cannot_finish_a_challenge_from_before_a_password_change()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-named-change", IPAddress.Parse("192.0.2.58"), password: Password);
        using var authenticator = new PasskeyAuthenticator();
        await LegacyProfile.RegisterPasskeyAsync(owner.Client, authenticator, "Named key");
        var pending = vault.CreateSession(source: IPAddress.Parse("192.0.2.59"));
        var options = await SignInProof.PostPasskeyAsync(pending.Client, "/Account/PasskeyLoginBegin", new { username = "it-generation-named-change" });
        await LegacyProfile.ChangePasswordAsync(owner.Client, Password, ReplacementPassword);

        var result = await SignInProof.PostPasskeyAsync(pending.Client, "/Account/PasskeyLoginComplete", new { credential = authenticator.Assert(options) });

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Null(pending.SessionCookie);
        var account = await LegacyProfile.AccountAsync(vault, "it-generation-named-change");
        Assert.Equal(0, Assert.Single(await LegacyProfile.PasskeysAsync(vault, account.Id)).SignCount);
    }

    [Fact]
    public async Task A_removed_discoverable_key_cannot_finish_its_anonymous_challenge()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-removed", IPAddress.Parse("192.0.2.60"));
        using var authenticator = new PasskeyAuthenticator();
        await LegacyProfile.RegisterPasskeyAsync(owner.Client, authenticator, "Removed key");
        var account = await LegacyProfile.AccountAsync(vault, "it-generation-removed");
        var key = Assert.Single(await LegacyProfile.PasskeysAsync(vault, account.Id));
        var pending = vault.CreateSession(source: IPAddress.Parse("192.0.2.61"));
        var options = await SignInProof.PostPasskeyAsync(pending.Client, "/Account/PasskeyLoginBegin", new { });
        await LegacyProfile.RemovePasskeyAsync(owner.Client, key.Id);

        var result = await SignInProof.PostPasskeyAsync(pending.Client, "/Account/PasskeyLoginComplete", new { credential = authenticator.Assert(options) });

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Null(pending.SessionCookie);
    }

    [Fact]
    public async Task A_password_change_keeps_a_fresh_current_session_and_refuses_the_copied_old_cookie()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-password", IPAddress.Parse("192.0.2.40"), password: Password);
        var original = SessionApi.PrincipalOf(vault, owner);
        var copied = vault.CreateSession(owner.SessionCookie, IPAddress.Parse("192.0.2.41"));

        await LegacyProfile.ChangePasswordAsync(owner.Client, Password, ReplacementPassword);

        var replacement = SessionApi.PrincipalOf(vault, owner);
        Assert.Equal(1, replacement.GetSessionStampOrNull()!.CredentialGeneration);
        Assert.Equal(original.GetSessionStampOrNull()!.RevocationVersion, replacement.GetSessionStampOrNull()!.RevocationVersion);
        Assert.NotEqual(original.GetSessionId(), replacement.GetSessionId());
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync("/api/ui/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await copied.Client.GetAsync("/api/ui/me")).StatusCode);
        Assert.Single((await owner.Client.GetFromJsonAsync<SessionsResponse>("/api/ui/sessions"))!.Sessions);
    }

    [Fact]
    public async Task An_active_email_change_advances_the_generation_and_replaces_the_current_session()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-email", IPAddress.Parse("192.0.2.42"));
        var original = SessionApi.PrincipalOf(vault, owner);

        await LegacyProfile.UpdateDetailsAsync(owner.Client, "Email Owner", "generation-owner@example.test");

        Assert.Equal(1, SessionApi.PrincipalOf(vault, owner).GetSessionStampOrNull()!.CredentialGeneration);
        Assert.NotEqual(original.GetSessionId(), SessionApi.PrincipalOf(vault, owner).GetSessionId());
        Assert.Equal(1, (await LegacyProfile.AccountAsync(vault, "it-generation-email")).CredentialGeneration);
    }

    [Fact]
    public async Task A_display_name_change_preserves_the_original_generation_version_and_session_id()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-cosmetic", IPAddress.Parse("192.0.2.43"));
        var account = await LegacyProfile.AccountAsync(vault, "it-generation-cosmetic");
        var original = SessionApi.PrincipalOf(vault, owner);

        await LegacyProfile.UpdateDetailsAsync(owner.Client, "Renamed Owner", account.Email);

        var renewed = SessionApi.PrincipalOf(vault, owner);
        Assert.Equal(original.GetSessionStampOrNull(), renewed.GetSessionStampOrNull());
        Assert.Equal(original.GetSessionId(), renewed.GetSessionId());
        Assert.Equal(0, (await LegacyProfile.AccountAsync(vault, account.Username)).CredentialGeneration);
    }

    [Fact]
    public async Task An_avatar_change_preserves_the_original_generation_version_and_session_id()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-avatar", IPAddress.Parse("192.0.2.56"));
        var original = SessionApi.PrincipalOf(vault, owner);

        await LegacyProfile.UploadAvatarAsync(owner.Client, LegacyProfile.TinyPng);

        var renewed = SessionApi.PrincipalOf(vault, owner);
        Assert.Equal(original.GetSessionStampOrNull(), renewed.GetSessionStampOrNull());
        Assert.Equal(original.GetSessionId(), renewed.GetSessionId());
        Assert.Equal(0, (await LegacyProfile.AccountAsync(vault, "it-generation-avatar")).CredentialGeneration);
    }

    [Fact]
    public async Task Recovery_replacement_refuses_a_copied_session_and_retains_only_the_new_device()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-recovery", IPAddress.Parse("192.0.2.44"));
        var original = SessionApi.PrincipalOf(vault, owner);
        var copied = vault.CreateSession(owner.SessionCookie, IPAddress.Parse("192.0.2.45"));

        await LegacyProfile.GenerateRecoveryCodesAsync(owner.Client);

        Assert.Equal(1, SessionApi.PrincipalOf(vault, owner).GetSessionStampOrNull()!.CredentialGeneration);
        Assert.NotEqual(original.GetSessionId(), SessionApi.PrincipalOf(vault, owner).GetSessionId());
        Assert.Equal(HttpStatusCode.Unauthorized, (await copied.Client.GetAsync("/api/ui/me")).StatusCode);
        Assert.Equal(SessionApi.PrincipalOf(vault, owner).GetSessionId(), (await SessionApi.CurrentAsync(owner)).SessionId);
    }

    [Fact]
    public async Task Authenticator_reset_advances_the_generation_and_ends_the_current_and_copied_sessions()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-reset", IPAddress.Parse("192.0.2.46"));
        var copied = vault.CreateSession(owner.SessionCookie, IPAddress.Parse("192.0.2.47"));

        await LegacyProfile.ResetAuthenticatorAsync(owner.Client);

        Assert.Null(owner.SessionCookie);
        Assert.Equal(1, (await LegacyProfile.AccountAsync(vault, "it-generation-reset")).CredentialGeneration);
        Assert.Equal(HttpStatusCode.Unauthorized, (await copied.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task Passkey_addition_replaces_the_session_under_the_committed_generation()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-key-add", IPAddress.Parse("192.0.2.48"));
        var original = SessionApi.PrincipalOf(vault, owner);
        using var authenticator = new PasskeyAuthenticator();

        await LegacyProfile.RegisterPasskeyAsync(owner.Client, authenticator, "Generation key");

        var registered = SessionApi.PrincipalOf(vault, owner);
        Assert.Equal(1, registered.GetSessionStampOrNull()!.CredentialGeneration);
        Assert.NotEqual(original.GetSessionId(), registered.GetSessionId());
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task Passkey_removal_replaces_the_session_under_the_committed_generation()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-key-remove", IPAddress.Parse("192.0.2.57"));
        using var authenticator = new PasskeyAuthenticator();
        await LegacyProfile.RegisterPasskeyAsync(owner.Client, authenticator, "Removed key");
        var registered = SessionApi.PrincipalOf(vault, owner);
        var account = await LegacyProfile.AccountAsync(vault, "it-generation-key-remove");
        var key = Assert.Single(await LegacyProfile.PasskeysAsync(vault, account.Id));

        await LegacyProfile.RemovePasskeyAsync(owner.Client, key.Id);

        var removed = SessionApi.PrincipalOf(vault, owner);
        Assert.Equal(2, removed.GetSessionStampOrNull()!.CredentialGeneration);
        Assert.NotEqual(registered.GetSessionId(), removed.GetSessionId());
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task A_pending_password_proof_is_refused_before_consuming_a_factor_after_a_credential_change()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-pending", IPAddress.Parse("192.0.2.49"), password: Password);
        var pending = vault.CreateSession(source: IPAddress.Parse("192.0.2.50"));
        await SignInProof.BeginPasswordAsync(pending.Client, "it-generation-pending", Password);
        var account = await LegacyProfile.AccountAsync(vault, "it-generation-pending");

        await LegacyProfile.GenerateRecoveryCodesAsync(owner.Client);

        Assert.Equal(HttpStatusCode.Redirect, (await pending.Client.GetAsync("/Account/Totp")).StatusCode);
        Assert.Null(pending.SessionCookie);
        Assert.Equal(account.LastTotpStep, (await LegacyProfile.AccountAsync(vault, account.Username)).LastTotpStep);
    }

    [Fact]
    public async Task A_named_passkey_proof_started_before_signing_out_everywhere_cannot_spend_its_counter()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-named", IPAddress.Parse("192.0.2.51"));
        using var authenticator = new PasskeyAuthenticator();
        await LegacyProfile.RegisterPasskeyAsync(owner.Client, authenticator, "Named key");
        var pending = vault.CreateSession(source: IPAddress.Parse("192.0.2.52"));
        var options = await SignInProof.PostPasskeyAsync(pending.Client, "/Account/PasskeyLoginBegin", new { username = "it-generation-named" });
        await vault.RevokeAllSessionsOfAsync("it-generation-named");

        var result = await SignInProof.PostPasskeyAsync(pending.Client, "/Account/PasskeyLoginComplete", new { credential = authenticator.Assert(options) });

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Null(pending.SessionCookie);
        var account = await LegacyProfile.AccountAsync(vault, "it-generation-named");
        Assert.Equal(0, Assert.Single(await LegacyProfile.PasskeysAsync(vault, account.Id)).SignCount);
    }

    [Fact]
    public async Task An_unchanged_discoverable_key_can_finish_an_anonymous_challenge_started_before_a_password_change()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-discover", IPAddress.Parse("192.0.2.53"), password: Password);
        using var authenticator = new PasskeyAuthenticator();
        await LegacyProfile.RegisterPasskeyAsync(owner.Client, authenticator, "Discoverable key");
        var pending = vault.CreateSession(source: IPAddress.Parse("192.0.2.54"));
        var options = await SignInProof.PostPasskeyAsync(pending.Client, "/Account/PasskeyLoginBegin", new { });
        await LegacyProfile.ChangePasswordAsync(owner.Client, Password, ReplacementPassword);

        var result = await SignInProof.PostPasskeyAsync(pending.Client, "/Account/PasskeyLoginComplete", new { credential = authenticator.Assert(options) });

        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal(2, SessionApi.PrincipalOf(vault, pending).GetSessionStampOrNull()!.CredentialGeneration);
        Assert.Equal(HttpStatusCode.OK, (await pending.Client.GetAsync("/api/ui/me")).StatusCode);
    }
}
