using System.Net;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Mfa;
using EclipsVault.Core.Application.Profile;
using EclipsVault.Core.Application.StepUp;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class PendingProofTests(VaultFixture vault)
{
    private const string Password = "It-pending-proof-password-2026!";
    private const string ReplacementPassword = "It-pending-proof-replacement-2026!";

    [Fact]
    public async Task A_valid_authenticator_post_cannot_complete_a_password_proof_from_retired_credentials()
    {
        await vault.SignInNewAccountAsync("it-proof-totp", IPAddress.Parse("192.0.2.110"), password: Password);
        var account = await ProfileHttp.AccountAsync(vault, "it-proof-totp");
        var pending = vault.CreateSession(source: IPAddress.Parse("192.0.2.111"));
        await SignInProof.BeginPasswordAsync(pending.Client, account.Username, Password);
        var token = VaultFixture.FormTokenOf(await pending.Client.GetStringAsync("/Account/Totp"), "/Account/Totp");
        await ChangePasswordAsync(account.Id, 0);

        var response = await pending.Client.PostAsync("/Account/Totp", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Code"] = Totp.NextCode(account.TotpSecret!),
        }));

        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        Assert.Null(pending.SessionCookie);
        Assert.Equal(account.LastTotpStep, (await ProfileHttp.AccountAsync(vault, account.Username)).LastTotpStep);
    }

    [Fact]
    public async Task A_valid_recovery_post_cannot_spend_a_code_through_a_retired_password_proof()
    {
        await vault.SignInNewAccountAsync("it-proof-recovery", IPAddress.Parse("192.0.2.112"), password: Password);
        var account = await ProfileHttp.AccountAsync(vault, "it-proof-recovery");
        using var setup = vault.Services.CreateScope();
        var set = await setup.ServiceProvider.GetRequiredService<IMfaRecoveryService>().GenerateAsync(account.Id, 0, default);
        var pending = vault.CreateSession(source: IPAddress.Parse("192.0.2.113"));
        await SignInProof.BeginPasswordAsync(pending.Client, account.Username, Password);
        var token = VaultFixture.FormTokenOf(await pending.Client.GetStringAsync("/Account/RecoveryCode"), "/Account/RecoveryCode");
        await ChangePasswordAsync(account.Id, set.CredentialGeneration);

        var response = await pending.Client.PostAsync("/Account/RecoveryCode", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Code"] = set.Codes[0],
        }));

        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        Assert.Null(pending.SessionCookie);
        Assert.All(await ProfileHttp.RecoveryCodesAsync(vault, account.Id), code => Assert.Null(code.UsedAtUtc));
    }

    [Fact]
    public async Task A_valid_enrollment_post_cannot_finish_a_retired_password_proof()
    {
        var owner = await vault.SignInNewAccountAsync("it-proof-enrollment", IPAddress.Parse("192.0.2.114"), password: Password);
        await ProfileHttp.ResetAuthenticatorAsync(owner);
        var account = await ProfileHttp.AccountAsync(vault, "it-proof-enrollment");
        var pending = vault.CreateSession(source: IPAddress.Parse("192.0.2.115"));
        await SignInProof.BeginPasswordAsync(pending.Client, account.Username, Password);
        var token = VaultFixture.FormTokenOf(await pending.Client.GetStringAsync("/Account/EnrollTotp"), "/Account/EnrollTotp");
        var enrollment = await ProfileHttp.AccountAsync(vault, account.Username);
        await ChangePasswordAsync(account.Id, 1);

        var response = await pending.Client.PostAsync("/Account/EnrollTotp", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Code"] = Totp.Code(enrollment.TotpSecret!, Totp.CurrentStep()),
        }));

        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        Assert.Null(pending.SessionCookie);
        Assert.False((await ProfileHttp.AccountAsync(vault, account.Username)).TotpEnabled);
    }

    [Fact]
    public async Task Step_up_cannot_spend_a_factor_after_its_original_revocation_version_changes()
    {
        var owner = await vault.SignInNewAccountAsync("it-proof-step-up", IPAddress.Parse("192.0.2.116"));
        var principal = SessionApi.PrincipalOf(vault, owner);
        var account = await ProfileHttp.AccountAsync(vault, "it-proof-step-up");
        using var staleRequest = vault.Services.CreateScope();
        await staleRequest.ServiceProvider.GetRequiredService<IUserRepository>().FindByIdAsync(account.Id, default);
        await vault.RevokeAllSessionsOfAsync(account.Username);

        await Assert.ThrowsAsync<StaleProofException>(() => staleRequest.ServiceProvider.GetRequiredService<IStepUpService>()
            .VerifyAsync(account.Id, principal.GetSessionStampOrNull()!, Totp.NextCode(account.TotpSecret!), default));

        Assert.Equal(account.LastTotpStep, (await ProfileHttp.AccountAsync(vault, account.Username)).LastTotpStep);
    }

    private async Task ChangePasswordAsync(Guid accountId, long generation)
    {
        using var request = vault.Services.CreateScope();
        await request.ServiceProvider.GetRequiredService<IProfileService>()
            .ChangePasswordAsync(accountId, generation, Password, ReplacementPassword, default);
    }
}
