using System.Net;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A role change or a disable ends the account's sessions in the database, so they stay ended even when
/// the session store holds the account's earlier revocation version: its call failed, or it was restored
/// from a snapshot taken before the change.
/// </summary>
[Collection(RedisVaultCollection.Name)]
public sealed class DurableSessionEndTests(RedisVaultFixture fixture)
{
    [Fact]
    public async Task A_demoted_administrators_earlier_session_stays_refused_when_the_store_holds_its_earlier_version()
    {
        var vault = fixture.Vault;
        var demoted = await vault.SignInNewAccountAsync("it-store-demoted", IPAddress.Parse("192.0.2.150"), ClearanceLevel.TopSecret);
        var account = await ProfileHttp.AccountAsync(vault, "it-store-demoted");
        var versionKey = RevocationVersionKey(account.Id);
        var earlierVersion = (await fixture.Redis.Database.KeyDumpAsync(versionKey))!;

        await UserAccounts.EditRoleAsync(await vault.AdminClientAsync(), account.Id, ClearanceLevel.Standard, "GLOBAL");
        await fixture.Redis.Database.KeyDeleteAsync(versionKey);
        await fixture.Redis.Database.KeyRestoreAsync(versionKey, earlierVersion, TimeSpan.FromHours(24));

        var refused = await demoted.Client.GetAsync(UserAccounts.UsersPage);
        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        Assert.Equal("/Account/Login", refused.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Re_enabling_revives_no_session_when_the_store_holds_the_version_from_before_the_disable()
    {
        var vault = fixture.Vault;
        var owner = await vault.SignInNewAccountAsync("it-store-disabled", IPAddress.Parse("192.0.2.151"));
        var account = await ProfileHttp.AccountAsync(vault, "it-store-disabled");
        var versionKey = RevocationVersionKey(account.Id);
        var earlierVersion = (await fixture.Redis.Database.KeyDumpAsync(versionKey))!;
        var admin = await vault.AdminClientAsync();

        await UserActions.SetEnabledAsync(admin, account.Id, enabled: false);
        await fixture.Redis.Database.KeyDeleteAsync(versionKey);
        await fixture.Redis.Database.KeyRestoreAsync(versionKey, earlierVersion, TimeSpan.FromHours(24));
        await UserActions.SetEnabledAsync(admin, account.Id, enabled: true);

        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Client.GetAsync("/api/ui/me")).StatusCode);
    }

    private string RevocationVersionKey(Guid accountId)
        => $"{fixture.Vault.Services.GetRequiredService<IOptions<RedisOptions>>().Value.SessionKeyPrefix}:revocation-version:{accountId:N}";
}
