using System.Net;
using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Mfa;
using EclipsVault.Core.Application.Passkeys;
using EclipsVault.Core.Application.Sessions;
using EclipsVault.Core.Application.StepUp;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Web.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(AuditFailureCollection.Name)]
public sealed class AuthenticationAuditFailureTests(AuditFailureFixture failure)
{
    [Fact]
    public async Task A_refused_sign_in_outcome_issues_no_session()
    {
        var vault = failure.Vault;
        await vault.SignInNewAccountAsync("it-ledger-totp", IPAddress.Parse("203.0.113.170"));
        await SetFailuresAsync("it-ledger-totp", 2);
        var account = await ProfileHttp.AccountAsync(vault, "it-ledger-totp");
        var before = await AuditTrail.HeadAsync(vault);
        using var request = vault.Services.CreateScope();
        var auth = request.ServiceProvider.GetRequiredService<IVaultAuthenticationService>();
        var stamp = await CurrentStampAsync(vault, account);
        var code = Totp.Code(account.TotpSecret!, Totp.CurrentStep() + 1);

        await failure.RejectAuditWritesAsync();
        try
        {
            // The step is the factor. Its sign-in is the outcome written at issuance, so a rejected
            // outcome spends the step and still issues no session.
            var proven = await auth.VerifyTotpAsync(account.Id, stamp, code, default);
            var http = new DefaultHttpContext { RequestServices = request.ServiceProvider };
            await Assert.ThrowsAsync<AuditWriteFailedException>(() => request.ServiceProvider.GetRequiredService<SessionIssuer>()
                .SignInAsync(http, proven!, stamp, new SignInAudit(AuditAction.LoginSucceeded, "Password + TOTP"), default));
            Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        await request.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().SaveChangesAsync();
        var stored = await ProfileHttp.AccountAsync(vault, "it-ledger-totp");
        Assert.NotEqual(account.LastTotpStep, stored.LastTotpStep);
        Assert.Equal(0, stored.FailedAccessCount);
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), row => row.Action == AuditAction.LoginSucceeded);
    }

    [Fact]
    public async Task A_refused_recovery_sign_in_preserves_the_unused_code_and_failures()
    {
        var vault = failure.Vault;
        await vault.SignInNewAccountAsync("it-ledger-recovery", IPAddress.Parse("203.0.113.171"));
        var account = await ProfileHttp.AccountAsync(vault, "it-ledger-recovery");
        using var request = vault.Services.CreateScope();
        var recovery = await request.ServiceProvider.GetRequiredService<IMfaRecoveryService>()
            .GenerateAsync(account.Id, account.CredentialGeneration, default);
        var codes = recovery.Codes;
        await SetFailuresAsync("it-ledger-recovery", 2);
        var auth = request.ServiceProvider.GetRequiredService<IVaultAuthenticationService>();
        var stamp = await CurrentStampAsync(vault, account);
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            await Assert.ThrowsAsync<AuditWriteFailedException>(() => auth.VerifyRecoveryCodeAsync(account.Id, stamp, codes[0], default));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        await request.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().SaveChangesAsync();
        Assert.All(await ProfileHttp.RecoveryCodesAsync(vault, account.Id), code => Assert.Null(code.UsedAtUtc));
        Assert.Equal(2, (await ProfileHttp.AccountAsync(vault, "it-ledger-recovery")).FailedAccessCount);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        Assert.NotNull(await auth.VerifyRecoveryCodeAsync(account.Id, stamp, codes[0], default));
    }

    [Fact]
    public async Task A_refused_enrollment_keeps_the_authenticator_pending_and_failures_unchanged()
    {
        var vault = failure.Vault;
        await vault.SignInNewAccountAsync("it-ledger-enroll", IPAddress.Parse("203.0.113.172"));
        using (var setup = vault.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            var pending = await db.Users.SingleAsync(user => user.Username == "it-ledger-enroll");
            pending.TotpEnabled = false;
            pending.LastTotpStep = null;
            pending.FailedAccessCount = 2;
            await db.SaveChangesAsync();
        }

        var account = await ProfileHttp.AccountAsync(vault, "it-ledger-enroll");
        var before = await AuditTrail.HeadAsync(vault);
        using var request = vault.Services.CreateScope();
        var auth = request.ServiceProvider.GetRequiredService<IVaultAuthenticationService>();
        var stamp = await CurrentStampAsync(vault, account);
        var code = Totp.Code(account.TotpSecret!, Totp.CurrentStep());

        await failure.RejectAuditWritesAsync();
        try
        {
            await Assert.ThrowsAsync<AuditWriteFailedException>(() => auth.CompleteTotpEnrollmentAsync(account.Id, stamp, code, default));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        await request.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().SaveChangesAsync();
        var stored = await ProfileHttp.AccountAsync(vault, "it-ledger-enroll");
        Assert.False(stored.TotpEnabled);
        Assert.Null(stored.LastTotpStep);
        Assert.Equal(account.TotpSecret, stored.TotpSecret);
        Assert.Equal(2, stored.FailedAccessCount);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task A_refused_failure_event_does_not_lock_the_account_or_reset_its_counter()
    {
        var vault = failure.Vault;
        await vault.SignInNewAccountAsync("it-ledger-failure", IPAddress.Parse("203.0.113.173"));
        await SetFailuresAsync("it-ledger-failure", LockoutPolicy.Default.MaxFailedAttempts - 1);
        var account = await ProfileHttp.AccountAsync(vault, "it-ledger-failure");
        var before = await AuditTrail.HeadAsync(vault);
        using var request = vault.Services.CreateScope();
        var auth = request.ServiceProvider.GetRequiredService<IVaultAuthenticationService>();
        var stamp = await CurrentStampAsync(vault, account);

        await failure.RejectAuditWritesAsync();
        try
        {
            await Assert.ThrowsAsync<AuditWriteFailedException>(() => auth.ValidateCredentialsAsync(account.Username, "wrong-password", default));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        await request.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().SaveChangesAsync();
        var stored = await ProfileHttp.AccountAsync(vault, "it-ledger-failure");
        Assert.Equal(account.FailedAccessCount, stored.FailedAccessCount);
        Assert.Null(stored.LockedUntilUtc);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task A_refused_step_up_event_leaves_its_authenticator_code_unspent()
    {
        var vault = failure.Vault;
        await vault.SignInNewAccountAsync("it-ledger-step-up", IPAddress.Parse("203.0.113.174"));
        var account = await ProfileHttp.AccountAsync(vault, "it-ledger-step-up");
        var before = await AuditTrail.HeadAsync(vault);
        using var request = vault.Services.CreateScope();
        var stepUp = request.ServiceProvider.GetRequiredService<IStepUpService>();
        var stamp = await CurrentStampAsync(vault, account);
        var code = Totp.Code(account.TotpSecret!, Totp.CurrentStep() + 1);

        await failure.RejectAuditWritesAsync();
        try
        {
            await Assert.ThrowsAsync<AuditWriteFailedException>(() => stepUp.VerifyAsync(account.Id, stamp, code, default));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        await request.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().SaveChangesAsync();
        Assert.Equal(account.LastTotpStep, (await ProfileHttp.AccountAsync(vault, "it-ledger-step-up")).LastTotpStep);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        Assert.True(await stepUp.VerifyAsync(account.Id, stamp, code, default));
    }

    [Fact]
    public async Task A_refused_passkey_outcome_issues_no_session()
    {
        var vault = failure.Vault;
        var owner = await vault.SignInNewAccountAsync("it-ledger-passkey", IPAddress.Parse("203.0.113.175"));
        var account = await ProfileHttp.AccountAsync(vault, "it-ledger-passkey");
        using var authenticator = new PasskeyAuthenticator();
        await ProfileHttp.RegisterPasskeyAsync(owner, authenticator, "Rollback key");
        var key = Assert.Single(await ProfileHttp.PasskeysAsync(vault, account.Id));
        await SetFailuresAsync("it-ledger-passkey", 2);
        using var request = vault.Services.CreateScope();
        var passkeys = request.ServiceProvider.GetRequiredService<IPasskeyService>();
        var ceremony = await passkeys.BeginAssertionAsync(account.Username, default);
        var response = JsonSerializer.Serialize(authenticator.Assert(JsonSerializer.Deserialize<JsonElement>(ceremony.OptionsJson)));
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var proved = await passkeys.CompleteAssertionAsync(ceremony.Challenge, ceremony.NamedAccount, response, default);
            var http = new DefaultHttpContext { RequestServices = request.ServiceProvider };
            await Assert.ThrowsAsync<AuditWriteFailedException>(() => request.ServiceProvider.GetRequiredService<SessionIssuer>()
                .SignInAsync(http, proved.User!, proved.Stamp!, new SignInAudit(AuditAction.PasskeyLogin, "Passwordless sign-in with a passkey"), default));
            Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        await request.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().SaveChangesAsync();
        Assert.NotEqual(key.SignCount, Assert.Single(await ProfileHttp.PasskeysAsync(vault, account.Id)).SignCount);
        Assert.Equal(0, (await ProfileHttp.AccountAsync(vault, "it-ledger-passkey")).FailedAccessCount);
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), row => row.Action == AuditAction.PasskeyLogin);
    }

    [Fact]
    public async Task A_refused_passkey_failure_event_preserves_the_pre_lockout_counter()
    {
        var vault = failure.Vault;
        var owner = await vault.SignInNewAccountAsync("it-ledger-passkey-failure", IPAddress.Parse("203.0.113.176"));
        var account = await ProfileHttp.AccountAsync(vault, "it-ledger-passkey-failure");
        using var authenticator = new PasskeyAuthenticator();
        await ProfileHttp.RegisterPasskeyAsync(owner, authenticator, "Failed proof key");
        await SetFailuresAsync(account.Username, 4);
        using var request = vault.Services.CreateScope();
        var passkeys = request.ServiceProvider.GetRequiredService<IPasskeyService>();
        var ceremony = await passkeys.BeginAssertionAsync(account.Username, default);
        var response = JsonSerializer.Serialize(authenticator.AssertWithWrongSignature(JsonSerializer.Deserialize<JsonElement>(ceremony.OptionsJson)));
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            await Assert.ThrowsAsync<AuditWriteFailedException>(() => passkeys.CompleteAssertionAsync(ceremony.Challenge, ceremony.NamedAccount, response, default));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        await request.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().SaveChangesAsync();
        var stored = await ProfileHttp.AccountAsync(vault, account.Username);
        Assert.Equal(4, stored.FailedAccessCount);
        Assert.Null(stored.LockedUntilUtc);
        Assert.Equal(0, Assert.Single(await ProfileHttp.PasskeysAsync(vault, account.Id)).SignCount);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    private async Task SetFailuresAsync(string username, int failures)
    {
        using var setup = failure.Vault.Services.CreateScope();
        var db = setup.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        var account = await db.Users.SingleAsync(user => user.Username == username);
        account.FailedAccessCount = failures;
        await db.SaveChangesAsync();
    }
    private static async Task<SessionStamp> CurrentStampAsync(VaultFixture vault, User account)
        => new((await ProfileHttp.AccountAsync(vault, account.Username)).CredentialGeneration,
            await vault.Services.GetRequiredService<ISessionRevocationService>().CaptureAsync(account.Id));

}
