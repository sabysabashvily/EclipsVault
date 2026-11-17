using System.Net;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The sign-in UI API when the trail refuses its rows (ADR-0032). Every call that records something
/// is refused with no session; a call that records nothing still answers. Break-glass is refused one
/// row at a time: the recorded session may stay, but no block is lifted without its row, and a later
/// request from the blocked source is still refused.
/// </summary>
[Collection(AuditFailureCollection.Name)]
public sealed class AuthAuditFailureTests(AuditFailureFixture failure)
{
    private const string Password = "It-auth-audit-password-2026!";

    private VaultFixture Vault => failure.Vault;

    [Fact]
    public async Task Every_sign_in_call_that_records_is_refused_and_the_others_still_answer()
    {
        var totp = await PendingAsync("it-auth-af-totp", "100.74.175.1", "100.74.175.2");
        var owner = await Vault.SignInNewAccountAsync("it-auth-af-recovery", IPAddress.Parse("100.74.175.3"), password: Password);
        var codes = await ProfileApi.RecoverySetAsync(owner.ProfileOwner!);
        var recovery = Vault.CreateSession(source: IPAddress.Parse("100.74.175.4"));
        var recoveryToken = await AuthApi.PendingAsync(recovery, "it-auth-af-recovery", Password);
        await Vault.SignInNewAccountAsync("it-auth-af-enroll", IPAddress.Parse("100.74.175.5"), password: Password);
        await WithoutAuthenticatorAsync("it-auth-af-enroll");
        var enroll = Vault.CreateSession(source: IPAddress.Parse("100.74.175.6"));
        var enrollToken = await AuthApi.PendingAsync(enroll, "it-auth-af-enroll", Password, next: "enroll");
        var keyOwner = await Vault.SignInNewAccountAsync("it-auth-af-passkey", IPAddress.Parse("100.74.175.7"), password: Password);
        using var authenticator = new PasskeyAuthenticator();
        await ProfileHttp.RegisterPasskeyAsync(keyOwner, authenticator, "Refused trail");
        await Vault.SignInNewAccountAsync("it-auth-af-recover", IPAddress.Parse("100.74.175.8"), ClearanceLevel.TopSecret, password: Password);
        var adminSecret = await AuthApi.TotpSecretAsync(Vault, "it-auth-af-recover");
        var signedIn = await Vault.SignInNewAccountAsync("it-auth-af-sign-out", IPAddress.Parse("100.74.175.9"), password: Password);
        var signOutToken = await UiApiRequest.AntiforgeryTokenAsync(signedIn.Client);
        var passkey = Vault.CreateSession(source: IPAddress.Parse("100.74.175.10"));
        var anonymous = await AuthApi.AnonymousTokenAsync(passkey);
        var recover = Vault.CreateSession(source: IPAddress.Parse("100.74.175.11"));
        var recoverToken = await AuthApi.AnonymousTokenAsync(recover, "/Account/Recover");

        await failure.RejectAuditWritesAsync();
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await Vault.CreateClient().GetAsync("/api/ui/auth/options")).StatusCode);
            await AuditUnavailableAsync(await AuthApi.PasswordAsync(Vault.CreateSession(source: IPAddress.Parse("100.74.175.12")),
                "it-auth-af-totp", "It-wrong-password-2026!"), null);
            await AuditUnavailableAsync(await AuthApi.PostAsync(totp.Browser, "/api/ui/auth/totp", totp.Token,
                new { code = Totp.NextCode(await AuthApi.TotpSecretAsync(Vault, "it-auth-af-totp")) }), totp.Browser);
            await AuditUnavailableAsync(await AuthApi.PostAsync(recovery, "/api/ui/auth/recovery-code", recoveryToken, new { code = codes[0] }), recovery);
            var secret = (await AuthApi.JsonAsync(await AuthApi.PostAsync(enroll, "/api/ui/auth/enrollment", enrollToken)))
                .GetProperty("secretBase32").GetString()!;
            await AuditUnavailableAsync(await AuthApi.PostAsync(enroll, "/api/ui/auth/enrollment/confirm", enrollToken,
                new { code = Totp.Code(secret, Totp.CurrentStep()) }), enroll);
            var options = (await AuthApi.JsonAsync(await AuthApi.PostAsync(passkey, "/api/ui/auth/passkey/begin", anonymous, new { })))
                .GetProperty("publicKey");
            await AuditUnavailableAsync(await AuthApi.PostAsync(passkey, "/api/ui/auth/passkey/complete", anonymous,
                new { credential = authenticator.Assert(options) }), passkey);
            await AuditUnavailableAsync(await AuthApi.PostAsync(recover, "/api/ui/auth/recover", recoverToken,
                new { username = "it-auth-af-recover", password = Password, code = Totp.NextCode(adminSecret) }), recover);
            Assert.Equal(HttpStatusCode.NoContent, (await AuthApi.PostAsync(signedIn, "/api/ui/auth/sign-out", signOutToken)).StatusCode);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        Assert.Null(signedIn.SessionCookie);
    }

    [Fact]
    public async Task A_refused_session_row_leaves_no_cookie_and_no_lift()
    {
        var recovery = await BlockedRecoveryAsync("it-auth-af-glass-session", "100.74.175.20", "100.74.175.21");

        var response = await RejectingAsync(AuditAction.LoginSucceeded, recovery.SendAsync);

        await UiApiFailureContractTests.AssertProblemAsync(response, 503, "audit_unavailable");
        Assert.Null(recovery.Browser.SessionCookie);
        await recovery.AssertStillBlockedAsync();
        var actions = await ProfileHttp.EventsAfterAsync(Vault, recovery.Before, recovery.AccountId);
        Assert.DoesNotContain(AuditAction.LoginSucceeded, actions);
        Assert.DoesNotContain(AuditAction.BreakGlassRecovery, actions);
        await recovery.ReleaseAsync();
    }

    [Fact]
    public async Task A_refused_recovery_row_keeps_the_recorded_session_and_the_block()
    {
        var recovery = await BlockedRecoveryAsync("it-auth-af-glass-recovery", "100.74.175.22", "100.74.175.23");

        var response = await RejectingAsync(AuditAction.BreakGlassRecovery, recovery.SendAsync);

        await UiApiFailureContractTests.AssertProblemAsync(response, 503, "audit_unavailable");
        Assert.NotNull(recovery.Browser.SessionCookie);
        await recovery.AssertStillBlockedAsync();
        var actions = await ProfileHttp.EventsAfterAsync(Vault, recovery.Before, recovery.AccountId);
        Assert.Contains(AuditAction.LoginSucceeded, actions);
        Assert.DoesNotContain(AuditAction.BreakGlassRecovery, actions);
        await recovery.ReleaseAsync();
    }

    [Fact]
    public async Task A_refused_lift_row_restores_the_exact_blocks_and_keeps_access_refused()
    {
        var recovery = await BlockedRecoveryAsync("it-auth-af-glass-lift", "100.74.175.24", "100.74.175.25");

        var response = await RejectingAsync(AuditAction.IpRangeUnblocked, recovery.SendAsync);

        await UiApiFailureContractTests.AssertProblemAsync(response, 503, "audit_unavailable");
        Assert.NotNull(recovery.Browser.SessionCookie);
        await recovery.AssertStillBlockedAsync();
        // The recorded session stays; only the network it came from stays refused.
        Assert.Equal(HttpStatusCode.OK, (await Vault.CreateSession(recovery.Browser.SessionCookie, IPAddress.Parse("100.74.175.26"))
            .Client.GetAsync("/api/ui/me")).StatusCode);
        var actions = await ProfileHttp.EventsAfterAsync(Vault, recovery.Before, recovery.AccountId);
        Assert.Contains(AuditAction.BreakGlassRecovery, actions);
        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(Vault, recovery.Before), row => row.Action == AuditAction.IpRangeUnblocked);
        await recovery.ReleaseAsync();
    }

    [Fact]
    public async Task A_lift_that_cannot_be_put_back_exactly_reports_recovery_incomplete_and_keeps_the_source_refused()
    {
        var recovery = await BlockedRecoveryAsync("it-auth-af-glass-incomplete", "100.74.175.27", "100.74.175.28");
        var blacklist = Vault.Services.GetRequiredService<IIpBlacklist>();

        HttpResponseMessage response;
        await failure.RejectAuditWritesOfAsync(AuditAction.IpRangeUnblocked);
        try
        {
            var held = await HeldAuditChain.TakeAsync(Vault);
            var call = recovery.SendAsync();
            try
            {
                // The password, the code, the session and the recovery row commit; the lift row waits.
                held = await AuthApi.HoldAfterWritersAsync(Vault, held, writers: 4);
                Assert.DoesNotContain(await blacklist.ListAsync(), block => block.Network == recovery.Network);
                await blacklist.BlockAsync(recovery.Source.ToString(), "raised meanwhile");
            }
            finally
            {
                await held.DisposeAsync();
            }

            response = await call;
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        var problem = await ProfileApi.AssertProblemAsync(response, 503, "recovery_incomplete");
        Assert.Equal("Your sign-in was recorded, but the vault could not finish restoring network access. This source remains blocked. Contact an administrator.",
            problem.GetProperty("title").GetString());
        Assert.NotNull(recovery.Browser.SessionCookie);
        Assert.Equal("raised meanwhile", Assert.Single(await blacklist.ListAsync(), block => block.Network == recovery.Network).Reason);
        // Lifting the block raised meanwhile cannot release the guard of the unsettled lift.
        Assert.NotNull(await blacklist.UnblockAsync(recovery.Network));
        await UiApiFailureContractTests.AssertProblemAsync(await recovery.Browser.Client.GetAsync("/api/ui/me"), 403, "blocked");
    }

    private async Task<(VaultSession Browser, string Token)> PendingAsync(string username, string createdFrom, string signingInFrom)
    {
        await Vault.SignInNewAccountAsync(username, IPAddress.Parse(createdFrom), password: Password);
        var browser = Vault.CreateSession(source: IPAddress.Parse(signingInFrom));
        return (browser, await AuthApi.PendingAsync(browser, username, Password));
    }

    private static async Task AuditUnavailableAsync(HttpResponseMessage response, VaultSession? browser)
    {
        await UiApiFailureContractTests.AssertProblemAsync(response, 503, "audit_unavailable");
        Assert.Null(browser?.SessionCookie);
    }

    private async Task<HttpResponseMessage> RejectingAsync(AuditAction action, Func<Task<HttpResponseMessage>> send)
    {
        await failure.RejectAuditWritesOfAsync(action);
        try
        {
            return await send();
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    private async Task WithoutAuthenticatorAsync(string username)
    {
        using var setup = Vault.Services.CreateScope();
        var db = setup.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        await db.Users.Where(user => user.Username == username).ExecuteUpdateAsync(set => set
            .SetProperty(user => user.TotpEnabled, false)
            .SetProperty(user => user.LastTotpStep, (long?)null));
    }

    /// <summary>A Top Secret account about to recover from its own blocked network, its block as it stands.</summary>
    private async Task<BlockedRecovery> BlockedRecoveryAsync(string username, string blocked, string createdFrom)
    {
        await Vault.SignInNewAccountAsync(username, IPAddress.Parse(createdFrom), ClearanceLevel.TopSecret, password: Password);
        var account = await ProfileHttp.AccountAsync(Vault, username);
        var source = IPAddress.Parse(blocked);
        var blacklist = Vault.Services.GetRequiredService<IIpBlacklist>();
        await blacklist.BlockAsync(blocked, "break-glass row probe");
        var browser = Vault.CreateSession(source: source);
        var token = await AuthApi.AnonymousTokenAsync(browser, "/Account/Recover");
        var blocks = (await blacklist.ListAsync()).Where(block => block.Network == blocked + "/32").ToList();
        return new BlockedRecovery(Vault, blacklist, browser, source, account.Id, blocks, await AuditTrail.HeadAsync(Vault),
            () => AuthApi.PostAsync(browser, "/api/ui/auth/recover", token, new { username, password = Password, code = Totp.NextCode(account.TotpSecret!) }));
    }

    private sealed record BlockedRecovery(
        VaultFixture Vault, IIpBlacklist Blacklist, VaultSession Browser, IPAddress Source, Guid AccountId,
        IReadOnlyList<BlockedRangeDto> Blocks, long Before, Func<Task<HttpResponseMessage>> SendAsync)
    {
        public string Network => Source + "/32";

        /// <summary>The block is exactly as it was, and a later request from the source is still refused.</summary>
        public async Task AssertStillBlockedAsync()
        {
            Assert.Equal(Blocks, (await Blacklist.ListAsync()).Where(block => block.Network == Network));
            await UiApiFailureContractTests.AssertProblemAsync(await Browser.Client.GetAsync("/api/ui/me"), 403, "blocked");
        }

        public async Task ReleaseAsync() => await (await Blacklist.LiftForRecoveryAsync(Source)).ReleaseAsync();
    }
}
