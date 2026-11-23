using System.Net;
using System.Net.Http.Json;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models.Ui;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class CredentialGenerationTests(VaultFixture vault)
{
    private const string Password = "It-generation-password-2026!";
    private const string ReplacementPassword = "It-replacement-password-2026!";

    /// <summary>A new account's generation once activation has advanced it.</summary>
    private const long Activated = 1;

    /// <summary>
    /// Passkey sign-in names no account until the assertion arrives. A password change that lands
    /// once that account is known refuses the sign-in and leaves the counter unspent.
    /// </summary>
    [Fact]
    public async Task A_discoverable_challenge_cannot_finish_a_password_change_that_lands_after_the_account_is_known()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-known-change", IPAddress.Parse("192.0.2.58"), password: Password);
        using var authenticator = new PasskeyAuthenticator();
        await ProfileHttp.RegisterPasskeyAsync(owner, authenticator, "Discoverable key");
        var account = await ProfileHttp.AccountAsync(vault, "it-generation-known-change");
        var pending = vault.CreateSession(source: IPAddress.Parse("192.0.2.59"));
        var options = await SignInProof.PostPasskeyAsync(pending.Client, "/api/ui/auth/passkey/begin", new { });
        var credential = authenticator.Assert(options.GetProperty("publicKey"));

        var result = await FinishOnceTheAccountIsKnownAsync(pending, credential,
            held => AuthApi.AdvanceCredentialGenerationAsync(held, account.Id));

        await UiApiFailureContractTests.AssertProblemAsync(result, 400, "passkey_failed");
        Assert.Null(pending.SessionCookie);
        Assert.Equal(0, Assert.Single(await ProfileHttp.PasskeysAsync(vault, account.Id)).SignCount);
    }

    [Fact]
    public async Task A_removed_discoverable_key_cannot_finish_its_anonymous_challenge()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-removed", IPAddress.Parse("192.0.2.60"));
        using var authenticator = new PasskeyAuthenticator();
        await ProfileHttp.RegisterPasskeyAsync(owner, authenticator, "Removed key");
        var account = await ProfileHttp.AccountAsync(vault, "it-generation-removed");
        var key = Assert.Single(await ProfileHttp.PasskeysAsync(vault, account.Id));
        var pending = vault.CreateSession(source: IPAddress.Parse("192.0.2.61"));
        var options = await SignInProof.PostPasskeyAsync(pending.Client, "/api/ui/auth/passkey/begin", new { });
        await ProfileHttp.RemovePasskeyAsync(owner, key.Id);

        var result = await SignInProof.SendPasskeyAsync(pending.Client, "/api/ui/auth/passkey/complete",
            new { credential = authenticator.Assert(options.GetProperty("publicKey")) });

        await UiApiFailureContractTests.AssertProblemAsync(result, 400, "passkey_failed");
        Assert.Null(pending.SessionCookie);
    }

    [Fact]
    public async Task A_password_change_keeps_a_fresh_current_session_and_refuses_the_copied_old_cookie()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-password", IPAddress.Parse("192.0.2.40"), password: Password);
        var original = SessionApi.PrincipalOf(vault, owner);
        var copied = vault.CreateSession(owner.SessionCookie, IPAddress.Parse("192.0.2.41"));

        await ProfileHttp.ChangePasswordAsync(owner, Password, ReplacementPassword);

        var replacement = SessionApi.PrincipalOf(vault, owner);
        Assert.Equal(original.GetSessionStampOrNull()!.CredentialGeneration + 1, replacement.GetSessionStampOrNull()!.CredentialGeneration);
        Assert.Equal(original.GetSessionStampOrNull()!.RevocationVersion, replacement.GetSessionStampOrNull()!.RevocationVersion);
        Assert.NotEqual(original.GetSessionId(), replacement.GetSessionId());
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync("/api/ui/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await copied.Client.GetAsync("/api/ui/me")).StatusCode);
        Assert.Single((await owner.Client.GetFromJsonAsync<SessionsResponse>("/api/ui/sessions"))!.Sessions);
    }

    [Fact]
    public async Task A_display_name_change_preserves_the_original_generation_version_and_session_id()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-cosmetic", IPAddress.Parse("192.0.2.43"));
        var account = await ProfileHttp.AccountAsync(vault, "it-generation-cosmetic");
        var original = SessionApi.PrincipalOf(vault, owner);

        await ProfileHttp.UpdateDisplayNameAsync(owner, "Renamed Owner");

        var renewed = SessionApi.PrincipalOf(vault, owner);
        Assert.Equal(original.GetSessionStampOrNull(), renewed.GetSessionStampOrNull());
        Assert.Equal(original.GetSessionId(), renewed.GetSessionId());
        Assert.Equal(account.CredentialGeneration, (await ProfileHttp.AccountAsync(vault, account.Username)).CredentialGeneration);
    }

    [Fact]
    public async Task An_avatar_change_preserves_the_original_generation_version_and_session_id()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-avatar", IPAddress.Parse("192.0.2.56"));
        var original = SessionApi.PrincipalOf(vault, owner);

        await ProfileHttp.UploadAvatarAsync(owner, ProfileHttp.TinyPng);

        var renewed = SessionApi.PrincipalOf(vault, owner);
        Assert.Equal(original.GetSessionStampOrNull(), renewed.GetSessionStampOrNull());
        Assert.Equal(original.GetSessionId(), renewed.GetSessionId());
        Assert.Equal(original.GetSessionStampOrNull()!.CredentialGeneration, (await ProfileHttp.AccountAsync(vault, "it-generation-avatar")).CredentialGeneration);
    }

    [Fact]
    public async Task Recovery_replacement_refuses_a_copied_session_and_retains_only_the_new_device()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-recovery", IPAddress.Parse("192.0.2.44"));
        var original = SessionApi.PrincipalOf(vault, owner);
        var copied = vault.CreateSession(owner.SessionCookie, IPAddress.Parse("192.0.2.45"));

        await ProfileHttp.GenerateRecoveryCodesAsync(owner);

        Assert.Equal(Activated + 1, SessionApi.PrincipalOf(vault, owner).GetSessionStampOrNull()!.CredentialGeneration);
        Assert.NotEqual(original.GetSessionId(), SessionApi.PrincipalOf(vault, owner).GetSessionId());
        Assert.Equal(HttpStatusCode.Unauthorized, (await copied.Client.GetAsync("/api/ui/me")).StatusCode);
        Assert.Equal(SessionApi.PrincipalOf(vault, owner).GetSessionId(), (await SessionApi.CurrentAsync(owner)).SessionId);
    }

    [Fact]
    public async Task Authenticator_reset_advances_the_generation_and_ends_the_current_and_copied_sessions()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-reset", IPAddress.Parse("192.0.2.46"));
        var copied = vault.CreateSession(owner.SessionCookie, IPAddress.Parse("192.0.2.47"));

        await ProfileHttp.ResetAuthenticatorAsync(owner);

        Assert.Null(owner.SessionCookie);
        Assert.Equal(Activated + 1, (await ProfileHttp.AccountAsync(vault, "it-generation-reset")).CredentialGeneration);
        Assert.Equal(HttpStatusCode.Unauthorized, (await copied.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task Passkey_addition_replaces_the_session_under_the_committed_generation()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-key-add", IPAddress.Parse("192.0.2.48"));
        var original = SessionApi.PrincipalOf(vault, owner);
        using var authenticator = new PasskeyAuthenticator();

        await ProfileHttp.RegisterPasskeyAsync(owner, authenticator, "Generation key");

        var registered = SessionApi.PrincipalOf(vault, owner);
        Assert.Equal(Activated + 1, registered.GetSessionStampOrNull()!.CredentialGeneration);
        Assert.NotEqual(original.GetSessionId(), registered.GetSessionId());
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task Passkey_removal_replaces_the_session_under_the_committed_generation()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-key-remove", IPAddress.Parse("192.0.2.57"));
        using var authenticator = new PasskeyAuthenticator();
        await ProfileHttp.RegisterPasskeyAsync(owner, authenticator, "Removed key");
        var registered = SessionApi.PrincipalOf(vault, owner);
        var account = await ProfileHttp.AccountAsync(vault, "it-generation-key-remove");
        var key = Assert.Single(await ProfileHttp.PasskeysAsync(vault, account.Id));

        await ProfileHttp.RemovePasskeyAsync(owner, key.Id);

        var removed = SessionApi.PrincipalOf(vault, owner);
        Assert.Equal(Activated + 2, removed.GetSessionStampOrNull()!.CredentialGeneration);
        Assert.NotEqual(registered.GetSessionId(), removed.GetSessionId());
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    [Fact]
    public async Task A_pending_password_proof_is_refused_before_consuming_a_factor_after_a_credential_change()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-pending", IPAddress.Parse("192.0.2.49"), password: Password);
        var pending = vault.CreateSession(source: IPAddress.Parse("192.0.2.50"));
        await SignInProof.BeginPasswordAsync(pending.Client, "it-generation-pending", Password);
        var account = await ProfileHttp.AccountAsync(vault, "it-generation-pending");

        await ProfileHttp.GenerateRecoveryCodesAsync(owner);

        Assert.Equal(HttpStatusCode.Redirect, (await pending.Client.GetAsync("/Account/Totp")).StatusCode);
        Assert.Null(pending.SessionCookie);
        Assert.NotEqual(account.LastTotpStep, (await ProfileHttp.AccountAsync(vault, account.Username)).LastTotpStep);
    }

    /// <summary>
    /// Signing out everywhere replaces the revocation version. When that lands after the assertion
    /// has identified the account, the proof is refused and its counter stays where it was.
    /// </summary>
    [Fact]
    public async Task A_discoverable_proof_cannot_spend_its_counter_when_sign_out_everywhere_lands_after_the_account_is_known()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-signout", IPAddress.Parse("192.0.2.51"));
        using var authenticator = new PasskeyAuthenticator();
        await ProfileHttp.RegisterPasskeyAsync(owner, authenticator, "Discoverable key");
        var account = await ProfileHttp.AccountAsync(vault, "it-generation-signout");
        var pending = vault.CreateSession(source: IPAddress.Parse("192.0.2.52"));
        var options = await SignInProof.PostPasskeyAsync(pending.Client, "/api/ui/auth/passkey/begin", new { });
        var credential = authenticator.Assert(options.GetProperty("publicKey"));

        var result = await FinishOnceTheAccountIsKnownAsync(pending, credential, async _ =>
            await vault.Services.GetRequiredService<ISessionRevocationService>().RevokeAsync(account.Id));

        await UiApiFailureContractTests.AssertProblemAsync(result, 400, "passkey_failed");
        Assert.Null(pending.SessionCookie);
        Assert.Equal(0, Assert.Single(await ProfileHttp.PasskeysAsync(vault, account.Id)).SignCount);
    }

    [Fact]
    public async Task An_unchanged_discoverable_key_can_finish_an_anonymous_challenge_started_before_a_password_change()
    {
        var owner = await vault.SignInNewAccountAsync("it-generation-discover", IPAddress.Parse("192.0.2.53"), password: Password);
        using var authenticator = new PasskeyAuthenticator();
        await ProfileHttp.RegisterPasskeyAsync(owner, authenticator, "Discoverable key");
        var pending = vault.CreateSession(source: IPAddress.Parse("192.0.2.54"));
        var options = await SignInProof.PostPasskeyAsync(pending.Client, "/api/ui/auth/passkey/begin", new { });
        await ProfileHttp.ChangePasswordAsync(owner, Password, ReplacementPassword);

        var result = await SignInProof.SendPasskeyAsync(pending.Client, "/api/ui/auth/passkey/complete",
            new { credential = authenticator.Assert(options.GetProperty("publicKey")) });

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(Activated + 2, SessionApi.PrincipalOf(vault, pending).GetSessionStampOrNull()!.CredentialGeneration);
        Assert.Equal(HttpStatusCode.OK, (await pending.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    /// <summary>
    /// Completes a discoverable passkey sign-in, pausing once the assertion has identified the
    /// account and before that reading is checked again. The chain is what the completion waits
    /// on, so <paramref name="change"/> is ordered without a clock.
    /// </summary>
    private async Task<HttpResponseMessage> FinishOnceTheAccountIsKnownAsync(
        VaultSession pending, object credential, Func<HeldAuditChain, Task> change)
    {
        var held = await HeldAuditChain.TakeAsync(vault);
        var finishing = SignInProof.SendPasskeyAsync(pending.Client, "/api/ui/auth/passkey/complete", new { credential });
        try
        {
            await held.WaitForQueuedWritersAsync(1);
            await change(held);
            await held.CommitAsync();
        }
        finally
        {
            await held.DisposeAsync();
        }

        return await finishing;
    }
}
