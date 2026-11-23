using System.Net;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A factor can pass and the account can still change before a session exists. The trail then
/// records a refusal, and it does not record a sign-in. Each runs through the sign-in UI API: the
/// audit chain is held after the factor's own row commits, so the change lands while issuance waits.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class SignInRefusalAuditTests(VaultFixture vault)
{
    private const string Password = "It-refusal-password-2026!";

    [Fact]
    public async Task A_credential_change_between_verification_and_issuance_records_a_refusal_and_no_session()
    {
        const string username = "it-refusal-totp";
        await vault.SignInNewAccountAsync(username, IPAddress.Parse("198.18.93.1"), password: Password);
        var account = await ProfileHttp.AccountAsync(vault, username);
        var browser = vault.CreateSession(source: IPAddress.Parse("198.18.93.11"));
        var token = await AuthApi.PendingAsync(browser, username, Password);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await ChangeBeforeIssuanceAsync(account.Id, writersFirst: 1,
            () => AuthApi.PostAsync(browser, "/api/ui/auth/totp", token, new { code = Totp.NextCode(account.TotpSecret!) }));

        await AssertRestartedAsync(response, browser);
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
        var browser = vault.CreateSession(source: IPAddress.Parse("198.18.93.12"));
        var token = await AuthApi.PendingAsync(browser, username, Password);
        var before = await AuditTrail.HeadAsync(vault);

        using var change = vault.Services.CreateScope();
        var heldAccount = await change.ServiceProvider.GetRequiredService<IUserRepository>().FindByIdAsync(account.Id, default);
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chain = await HeldAuditChain.TakeAsync(vault);
        var signingIn = AuthApi.PostAsync(browser, "/api/ui/auth/totp", token, new { code = Totp.NextCode(account.TotpSecret!) });
        var changing = Task.FromResult(false);
        try
        {
            try
            {
                // The factor holds the account and waits for the chain; the change queues for the account behind it.
                await chain.WaitForQueuedWritersAsync(1);
                changing = change.ServiceProvider.GetRequiredService<IAuthenticationAttempt>().RunAsync(heldAccount!, async _ =>
                {
                    heldAccount!.CredentialGeneration++;
                    holding.SetResult();
                    await release.Task;
                    return true;
                }, default);
                await LockWaiters.WaitForAsync(vault, 2);
            }
            finally
            {
                await chain.DisposeAsync();
            }

            // The factor has committed and the change holds the account; issuance now waits for it.
            await holding.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await LockWaiters.WaitForAsync(vault, 1);
        }
        finally
        {
            release.TrySetResult();
        }

        await changing;
        await AssertRestartedAsync(await signingIn, browser);
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
        try
        {
            var browser = vault.CreateSession(source: source);
            var token = await AuthApi.AnonymousTokenAsync(browser, "/Account/Recover");
            var before = await AuditTrail.HeadAsync(vault);

            // The password stage and the authenticator code each commit before issuance.
            var response = await ChangeBeforeIssuanceAsync(account.Id, writersFirst: 2,
                () => AuthApi.PostAsync(browser, "/api/ui/auth/recover", token, new { username, password = Password, code = Totp.NextCode(account.TotpSecret!) }));

            await ProfileApi.AssertProblemAsync(response, 400, "recovery_failed");
            Assert.Null(browser.SessionCookie);
            Assert.True(await blacklist.IsBlockedAsync(source));
            Assert.Equal(HttpStatusCode.Forbidden, (await browser.Client.GetAsync("/api/ui/me")).StatusCode);
            var actions = await ProfileHttp.EventsAfterAsync(vault, before, account.Id);
            Assert.Contains(AuditAction.SignInRefused, actions);
            Assert.DoesNotContain(AuditAction.LoginSucceeded, actions);
            Assert.DoesNotContain(AuditAction.BreakGlassRecovery, actions);
        }
        finally
        {
            await (await blacklist.LiftForRecoveryAsync(source)).ReleaseAsync();
        }
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
        var browser = vault.CreateSession(source: source);
        var token = await AuthApi.AnonymousTokenAsync(browser, "/Account/Recover");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await AuthApi.PostAsync(browser, "/api/ui/auth/recover", token,
            new { username, password = Password, code = Totp.NextCode(account.TotpSecret!) });

        Assert.True((await AuthApi.JsonAsync(response)).GetProperty("blockLifted").GetBoolean());
        Assert.NotNull(browser.SessionCookie);
        Assert.False(await blacklist.IsBlockedAsync(source));
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/api/ui/me")).StatusCode);
        var actions = await ProfileHttp.EventsAfterAsync(vault, before, account.Id);
        Assert.Contains(AuditAction.LoginSucceeded, actions);
        Assert.Contains(AuditAction.BreakGlassRecovery, actions);
        Assert.DoesNotContain(AuditAction.SignInRefused, actions);
        Assert.Contains(await AuditTrail.RowsAfterAsync(vault, before),
            row => row.Action == AuditAction.IpRangeUnblocked && row.ResourceName == "198.18.93.7/32");
    }

    [Fact]
    public async Task A_recovery_code_spent_before_a_refused_session_is_not_recorded_as_a_sign_in()
    {
        const string username = "it-refusal-recovery";
        var owner = await vault.SignInNewAccountAsync(username, IPAddress.Parse("198.18.93.4"), password: Password);
        var codes = await ProfileApi.RecoverySetAsync(owner.ProfileOwner!);
        var account = await ProfileHttp.AccountAsync(vault, username);
        var browser = vault.CreateSession(source: IPAddress.Parse("198.18.93.13"));
        var token = await AuthApi.PendingAsync(browser, username, Password);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await ChangeBeforeIssuanceAsync(account.Id, writersFirst: 1,
            () => AuthApi.PostAsync(browser, "/api/ui/auth/recovery-code", token, new { code = codes[0] }));

        await AssertRestartedAsync(response, browser);
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
        var browser = vault.CreateSession(source: IPAddress.Parse("198.18.93.14"));
        var token = await AuthApi.PendingAsync(browser, username, Password, next: "enroll");
        var secret = (await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/enrollment", token)))
            .GetProperty("secretBase32").GetString()!;
        var before = await AuditTrail.HeadAsync(vault);

        var response = await ChangeBeforeIssuanceAsync(account.Id, writersFirst: 1,
            () => AuthApi.PostAsync(browser, "/api/ui/auth/enrollment/confirm", token, new { code = Totp.Code(secret, Totp.CurrentStep()) }));

        await AssertRestartedAsync(response, browser);
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
        var browser = vault.CreateSession(source: IPAddress.Parse("198.18.93.15"));
        var token = await AuthApi.AnonymousTokenAsync(browser);
        var options = (await AuthApi.JsonAsync(await AuthApi.PostAsync(browser, "/api/ui/auth/passkey/begin", token, new { })))
            .GetProperty("publicKey");
        var credential = authenticator.Assert(options);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await ChangeBeforeIssuanceAsync(account.Id, writersFirst: 1,
            () => AuthApi.PostAsync(browser, "/api/ui/auth/passkey/complete", token, new { credential }));

        var problem = await ProfileApi.AssertProblemAsync(response, 409, "sign_in_restarted");
        Assert.Equal("Sign-in timed out. Try again.", problem.GetProperty("title").GetString());
        Assert.Null(browser.SessionCookie);
        var actions = await ProfileHttp.EventsAfterAsync(vault, before, account.Id);
        Assert.Contains(AuditAction.SignInRefused, actions);
        Assert.DoesNotContain(AuditAction.PasskeyLogin, actions);
    }

    /// <summary>
    /// Sends the call, lets its first <paramref name="writersFirst"/> audited commits through, and
    /// advances the account's credential generation while its session issuance waits for the chain.
    /// </summary>
    private async Task<HttpResponseMessage> ChangeBeforeIssuanceAsync(Guid accountId, int writersFirst, Func<Task<HttpResponseMessage>> send)
    {
        var held = await HeldAuditChain.TakeAsync(vault);
        var call = send();
        try
        {
            held = await AuthApi.HoldAfterWritersAsync(vault, held, writersFirst);
            await AuthApi.AdvanceCredentialGenerationAsync(held, accountId);
            await held.CommitAsync();
        }
        finally
        {
            await held.DisposeAsync();
        }

        return await call;
    }

    private static async Task AssertRestartedAsync(HttpResponseMessage response, VaultSession browser)
    {
        var problem = await ProfileApi.AssertProblemAsync(response, 409, "sign_in_restarted");
        Assert.Equal("Your account changed while you were signing in. Sign in again.", problem.GetProperty("title").GetString());
        Assert.Null(browser.SessionCookie);
    }
}
