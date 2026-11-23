using System.Net;
using System.Security.Cryptography;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.IntegrationTests;

internal sealed class SessionAccount(VaultFixture vault, string username, string password, VaultSession browser, string[] recoveryCodes)
{
    public VaultSession Browser { get; } = browser;

    public static async Task<SessionAccount> CreateAsync(VaultFixture vault, string username, string address,
        ClearanceLevel clearance = ClearanceLevel.Standard)
    {
        var password = "It-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var browser = await vault.SignInNewAccountAsync(username, IPAddress.Parse(address), clearance, password: password);
        var codes = await ProfileApi.RecoverySetAsync(browser.ProfileOwner!);
        return new SessionAccount(vault, username, password, browser, codes);
    }

    public async Task<VaultSession> OpenDeviceAsync(int recoveryCodeIndex, string address)
    {
        var session = vault.CreateSession(source: IPAddress.Parse(address));
        session.Client.DefaultRequestHeaders.UserAgent.ParseAdd("curl/8.0");
        var pending = await AuthApi.PendingAsync(session, username, password);
        var secondFactor = await AuthApi.PostAsync(session, "/api/ui/auth/recovery-code", pending, new { code = recoveryCodes[recoveryCodeIndex] });
        Assert.Equal(HttpStatusCode.OK, secondFactor.StatusCode);
        Assert.NotNull(session.SessionCookie);
        return session;
    }

}
