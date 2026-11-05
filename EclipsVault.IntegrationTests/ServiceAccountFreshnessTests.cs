using System.Net;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class ServiceAccountFreshnessTests(VaultFixture vault)
{
    [Fact]
    public async Task A_stale_administrator_cannot_issue_a_service_account_key()
    {
        var administrator = await vault.SignInNewAccountAsync("it-sa-stale-issue", IPAddress.Parse("198.18.111.1"), ClearanceLevel.TopSecret);
        var account = await ServiceAccountsApi.CreatedAsync(vault, administrator.Client, "it-sa-stale-key");
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, administrator, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("198.18.111.1"));

        await ServiceAccountsApi.IssueKeyAsync(stale.Client, account.Id);

        Assert.Empty((await ServiceAccountsApi.FindAsync(vault, "it-sa-stale-key"))!.Keys);
    }

    [Fact]
    public async Task A_stale_administrator_cannot_enable_a_service_account()
    {
        var administrator = await vault.SignInNewAccountAsync("it-sa-stale-enable", IPAddress.Parse("198.18.111.2"), ClearanceLevel.TopSecret);
        var account = await ServiceAccountsApi.CreatedAsync(vault, administrator.Client, "it-sa-stale-disabled");
        await ServiceAccountsApi.SetEnabledAsync(administrator.Client, account.Id, false);
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, administrator, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("198.18.111.2"));

        await ServiceAccountsApi.SetEnabledAsync(stale.Client, account.Id, true);

        Assert.True((await ServiceAccountsApi.FindAsync(vault, "it-sa-stale-disabled"))!.IsDisabled);
    }

}
