using System.Net;
using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Mfa;
using EclipsVault.Core.Application.Passkeys;
using EclipsVault.Core.Application.Profile;
using EclipsVault.Core.Application.Sessions;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Web.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A factor can pass and the account can still change before a session exists. The trail then
/// records a refusal, and it does not record a sign-in.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class SignInRefusalAuditTests(VaultFixture vault)
{
    private const string Password = "It-refusal-password-2026!";
    private const string ReplacementPassword = "It-refusal-replacement-2026!";

    [Fact]
    public async Task A_credential_change_between_verification_and_issuance_records_a_refusal_and_no_session()
    {
        const string username = "it-refusal-totp";
        await vault.SignInNewAccountAsync(username, IPAddress.Parse("198.18.93.1"), password: Password);
        var account = await ProfileHttp.AccountAsync(vault, username);
        using var proof = vault.Services.CreateScope();
        var authentication = proof.ServiceProvider.GetRequiredService<IVaultAuthenticationService>();
        var passwordStage = await authentication.ValidateCredentialsAsync(username, Password, default);
        var before = await AuditTrail.HeadAsync(vault);
        var proven = await authentication.VerifyTotpAsync(account.Id, passwordStage.Stamp!, Totp.NextCode(account.TotpSecret!), default);
        await ChangePasswordAsync(account.Id, account.CredentialGeneration);

        var http = new DefaultHttpContext { RequestServices = proof.ServiceProvider };
        Assert.False(await IssueAsync(proof.ServiceProvider, http, proven!, passwordStage.Stamp!, "Password + TOTP"));

        Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
        var actions = await ProfileHttp.EventsAfterAsync(vault, before, account.Id);
        Assert.Contains(AuditAction.SignInRefused, actions);
        Assert.DoesNotContain(AuditAction.LoginSucceeded, actions);
    }

    [Fact]
    public async Task A_credential_change_committing_while_issuance_waits_for_the_account_records_a_refusal()
    {
        const string username = "it-refusal-held-account";
        await vault.SignInNewAccountAsync(username, IPAddress.Parse("198.18.93.10"), password: Password);
        var account = await ProfileHttp.AccountAsync(vault, username);
        using var proof = vault.Services.CreateScope();
        var authentication = proof.ServiceProvider.GetRequiredService<IVaultAuthenticationService>();
        var passwordStage = await authentication.ValidateCredentialsAsync(username, Password, default);
        var proven = await authentication.VerifyTotpAsync(account.Id, passwordStage.Stamp!, Totp.NextCode(account.TotpSecret!), default);
        var before = await AuditTrail.HeadAsync(vault);

        using var change = vault.Services.CreateScope();
        var held = await change.ServiceProvider.GetRequiredService<IUserRepository>().FindByIdAsync(account.Id, default);
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var changing = change.ServiceProvider.GetRequiredService<IAuthenticationAttempt>().RunAsync(held!, async _ =>
        {
            held!.CredentialGeneration++;
            holding.SetResult();
            await release.Task;
            return true;
        }, default);
        await holding.Task;
        var http = new DefaultHttpContext { RequestServices = proof.ServiceProvider };
        var issuing = IssueAsync(proof.ServiceProvider, http, proven!, passwordStage.Stamp!, "Password + TOTP");
        // Long enough for issuance to read the account and reach the lock the change holds.
        await Task.Delay(TimeSpan.FromSeconds(1));
        release.SetResult();
        await changing;

        Assert.False(await issuing);
        Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
        var actions = await ProfileHttp.EventsAfterAsync(vault, before, account.Id);
        Assert.Contains(AuditAction.SignInRefused, actions);
        Assert.DoesNotContain(AuditAction.LoginSucceeded, actions);
    }

    [Fact]
    public async Task Break_glass_lifts_a_blocked_address_only_after_a_session_is_issued()
    {
        const string username = "it-refusal-break-glass";
        var source = IPAddress.Parse("198.18.93.2");
        await vault.SignInNewAccountAsync(username, IPAddress.Parse("198.18.93.3"), ClearanceLevel.TopSecret, password: Password);
        var account = await ProfileHttp.AccountAsync(vault, username);
        var blacklist = vault.Services.GetRequiredService<IIpBlacklist>();
        await blacklist.BlockAsync(source.ToString(), "refusal probe", default);
        using var proof = vault.Services.CreateScope();
        var authentication = proof.ServiceProvider.GetRequiredService<IVaultAuthenticationService>();
        var passwordStage = await authentication.ValidateCredentialsAsync(username, Password, default);
        var before = await AuditTrail.HeadAsync(vault);
        var proven = await authentication.VerifyBreakGlassAsync(account.Id, passwordStage.Stamp!, Totp.NextCode(account.TotpSecret!), default);
        await ChangePasswordAsync(account.Id, account.CredentialGeneration);
        var http = new DefaultHttpContext { RequestServices = proof.ServiceProvider };

        var finished = await new BreakGlassSignIn(
            proof.ServiceProvider.GetRequiredService<SessionIssuer>(),
            blacklist,
            proof.ServiceProvider.GetRequiredService<IAuditSink>())
            .FinishAsync(http, proven!, passwordStage.Stamp!, source, default);

        Assert.False(finished.SessionIssued);
        Assert.False(finished.BlockLifted);
        Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.True(await blacklist.IsBlockedAsync(source));
        var actions = await ProfileHttp.EventsAfterAsync(vault, before, account.Id);
        Assert.Contains(AuditAction.SignInRefused, actions);
        Assert.DoesNotContain(AuditAction.LoginSucceeded, actions);
        Assert.DoesNotContain(AuditAction.BreakGlassRecovery, actions);
    }

    [Fact]
    public async Task Break_glass_records_the_recovery_and_lifts_the_block_when_the_session_is_issued()
    {
        const string username = "it-refusal-break-glass-issued";
        var source = IPAddress.Parse("198.18.93.7");
        await vault.SignInNewAccountAsync(username, IPAddress.Parse("198.18.93.8"), ClearanceLevel.TopSecret, password: Password);
        var account = await ProfileHttp.AccountAsync(vault, username);
        var blacklist = vault.Services.GetRequiredService<IIpBlacklist>();
        await blacklist.BlockAsync(source.ToString(), "issued recovery", default);
        using var proof = vault.Services.CreateScope();
        var authentication = proof.ServiceProvider.GetRequiredService<IVaultAuthenticationService>();
        var passwordStage = await authentication.ValidateCredentialsAsync(username, Password, default);
        var before = await AuditTrail.HeadAsync(vault);
        var proven = await authentication.VerifyBreakGlassAsync(account.Id, passwordStage.Stamp!, Totp.NextCode(account.TotpSecret!), default);
        var http = new DefaultHttpContext { RequestServices = proof.ServiceProvider };

        var finished = await new BreakGlassSignIn(
            proof.ServiceProvider.GetRequiredService<SessionIssuer>(),
            blacklist,
            proof.ServiceProvider.GetRequiredService<IAuditSink>())
            .FinishAsync(http, proven!, passwordStage.Stamp!, source, default);

        Assert.True(finished.SessionIssued);
        Assert.True(finished.BlockLifted);
        Assert.False(await blacklist.IsBlockedAsync(source));
        var actions = await ProfileHttp.EventsAfterAsync(vault, before, account.Id);
        Assert.Contains(AuditAction.LoginSucceeded, actions);
        Assert.Contains(AuditAction.BreakGlassRecovery, actions);
        Assert.DoesNotContain(AuditAction.SignInRefused, actions);
    }

    [Fact]
    public async Task A_recovery_code_spent_before_a_refused_session_is_not_recorded_as_a_sign_in()
    {
        const string username = "it-refusal-recovery";
        await vault.SignInNewAccountAsync(username, IPAddress.Parse("198.18.93.4"), password: Password);
        var created = await ProfileHttp.AccountAsync(vault, username);
        using var setup = vault.Services.CreateScope();
        var generated = await setup.ServiceProvider.GetRequiredService<IMfaRecoveryService>()
            .GenerateAsync(created.Id, created.CredentialGeneration, default);
        var account = await ProfileHttp.AccountAsync(vault, username);
        using var proof = vault.Services.CreateScope();
        var authentication = proof.ServiceProvider.GetRequiredService<IVaultAuthenticationService>();
        var passwordStage = await authentication.ValidateCredentialsAsync(username, Password, default);
        var before = await AuditTrail.HeadAsync(vault);
        var proven = await authentication.VerifyRecoveryCodeAsync(account.Id, passwordStage.Stamp!, generated.Codes[0], default);
        await ChangePasswordAsync(account.Id, account.CredentialGeneration);

        var http = new DefaultHttpContext { RequestServices = proof.ServiceProvider };
        Assert.False(await IssueAsync(proof.ServiceProvider, http, proven!, passwordStage.Stamp!, "Password + recovery code"));

        Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
        var actions = await ProfileHttp.EventsAfterAsync(vault, before, account.Id);
        Assert.Contains(AuditAction.RecoveryCodeUsed, actions);
        Assert.Contains(AuditAction.SignInRefused, actions);
        Assert.DoesNotContain(AuditAction.LoginSucceeded, actions);
    }

    [Fact]
    public async Task Enrollment_confirmed_before_a_refused_session_is_not_recorded_as_a_sign_in()
    {
        const string username = "it-refusal-enroll";
        await vault.SignInNewAccountAsync(username, IPAddress.Parse("198.18.93.5"), password: Password);
        using (var setup = vault.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            var pending = await db.Users.SingleAsync(user => user.Username == username);
            pending.TotpEnabled = false;
            pending.LastTotpStep = null;
            await db.SaveChangesAsync();
        }

        var account = await ProfileHttp.AccountAsync(vault, username);
        using var proof = vault.Services.CreateScope();
        var authentication = proof.ServiceProvider.GetRequiredService<IVaultAuthenticationService>();
        var passwordStage = await authentication.ValidateCredentialsAsync(username, Password, default);
        Assert.Equal(CredentialStatus.RequiresTotpEnrollment, passwordStage.Status);
        var before = await AuditTrail.HeadAsync(vault);
        var proven = await authentication.CompleteTotpEnrollmentAsync(
            account.Id, passwordStage.Stamp!, Totp.Code(account.TotpSecret!, Totp.CurrentStep()), default);
        await ChangePasswordAsync(account.Id, account.CredentialGeneration);

        var http = new DefaultHttpContext { RequestServices = proof.ServiceProvider };
        Assert.False(await IssueAsync(proof.ServiceProvider, http, proven!, passwordStage.Stamp!, "Password + TOTP enrollment"));

        Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.True((await ProfileHttp.AccountAsync(vault, username)).TotpEnabled);
        var actions = await ProfileHttp.EventsAfterAsync(vault, before, account.Id);
        Assert.Contains(AuditAction.TotpEnrolled, actions);
        Assert.Contains(AuditAction.SignInRefused, actions);
        Assert.DoesNotContain(AuditAction.LoginSucceeded, actions);
    }

    [Fact]
    public async Task A_passkey_proved_before_a_refused_session_is_not_recorded_as_a_sign_in()
    {
        const string username = "it-refusal-passkey";
        var owner = await vault.SignInNewAccountAsync(username, IPAddress.Parse("198.18.93.6"), password: Password);
        using var authenticator = new PasskeyAuthenticator();
        await ProfileHttp.RegisterPasskeyAsync(owner, authenticator, "Refusal key");
        var account = await ProfileHttp.AccountAsync(vault, username);
        using var proof = vault.Services.CreateScope();
        var passkeys = proof.ServiceProvider.GetRequiredService<IPasskeyService>();
        var ceremony = await passkeys.BeginAssertionAsync(username, default);
        var response = JsonSerializer.Serialize(authenticator.Assert(JsonSerializer.Deserialize<JsonElement>(ceremony.OptionsJson)));
        var before = await AuditTrail.HeadAsync(vault);
        var result = await passkeys.CompleteAssertionAsync(ceremony.Challenge, ceremony.NamedAccount, response, default);
        Assert.True(result.Success);
        await ChangePasswordAsync(account.Id, account.CredentialGeneration);

        var http = new DefaultHttpContext { RequestServices = proof.ServiceProvider };
        Assert.False(await proof.ServiceProvider.GetRequiredService<SessionIssuer>().SignInAsync(
            http, result.User!, result.Stamp!, new SignInAudit(AuditAction.PasskeyLogin, "Passwordless sign-in with a passkey"), default));

        Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
        var actions = await ProfileHttp.EventsAfterAsync(vault, before, account.Id);
        Assert.Contains(AuditAction.SignInRefused, actions);
        Assert.DoesNotContain(AuditAction.PasskeyLogin, actions);
    }

    private static Task<bool> IssueAsync(IServiceProvider services, HttpContext http, UserDto user, SessionStamp stamp, string details)
        => services.GetRequiredService<SessionIssuer>().SignInAsync(
            http, user, stamp, new SignInAudit(AuditAction.LoginSucceeded, details), default);

    private async Task ChangePasswordAsync(Guid accountId, long generation)
    {
        using var request = vault.Services.CreateScope();
        await request.ServiceProvider.GetRequiredService<IProfileService>()
            .ChangePasswordAsync(accountId, generation, Password, ReplacementPassword, default);
    }
}
