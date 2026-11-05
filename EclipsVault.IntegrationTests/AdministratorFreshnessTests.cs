using System.Net;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class AdministratorFreshnessTests(VaultFixture vault)
{
    [Fact]
    public async Task A_stale_administrator_cannot_create_lasting_access()
    {
        var administrator = await vault.SignInNewAccountAsync("it-fresh-create-admin", IPAddress.Parse("198.18.110.1"), ClearanceLevel.TopSecret);
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, administrator, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("198.18.110.1"));

        var response = await UserAccounts.CreateAsync(stale.Client, "it-fresh-create-target");

        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        Assert.False(await db.Users.AnyAsync(account => account.Username == "it-fresh-create-target"));
        await AssertStepUpAsync(response, "code");
    }

    [Fact]
    public async Task A_stale_administrator_cannot_change_an_accounts_role()
    {
        var administrator = await vault.SignInNewAccountAsync("it-fresh-role-admin", IPAddress.Parse("198.18.110.2"), ClearanceLevel.TopSecret);
        await UserAccounts.CreateAsync(administrator.Client, "it-fresh-role-target");
        var target = await ProfileHttp.AccountAsync(vault, "it-fresh-role-target");
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, administrator, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("198.18.110.2"));

        var response = await UserAccounts.EditRoleAsync(stale.Client, target.Id, ClearanceLevel.TopSecret, "OTHER");

        var stored = await ProfileHttp.AccountAsync(vault, "it-fresh-role-target");
        Assert.Equal((ClearanceLevel.Standard, "GLOBAL", target.CredentialGeneration), (stored.Clearance, stored.ProjectKey, stored.CredentialGeneration));
        await AssertStepUpAsync(response, "code");
    }

    [Fact]
    public async Task A_stale_administrator_cannot_enable_an_account()
    {
        var administrator = await vault.SignInNewAccountAsync("it-fresh-enable-admin", IPAddress.Parse("198.18.109.1"), ClearanceLevel.TopSecret);
        await UserAccounts.CreateAsync(administrator.Client, "it-fresh-enable-target");
        var target = await ProfileHttp.AccountAsync(vault, "it-fresh-enable-target");
        await UserActions.SetEnabledAsync(administrator.Client, target.Id, enabled: false);
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, administrator, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("198.18.109.1"));

        var response = await UserActions.SetEnabledAsync(stale.Client, target.Id, enabled: true);

        Assert.True((await ProfileHttp.AccountAsync(vault, "it-fresh-enable-target")).IsDisabled);
        await AssertStepUpAsync(response, "code");
    }

    [Fact]
    public async Task A_stale_administrator_cannot_clear_an_account_lockout()
    {
        var administrator = await vault.SignInNewAccountAsync("it-fresh-unlock-admin", IPAddress.Parse("198.18.109.2"), ClearanceLevel.TopSecret);
        await UserAccounts.CreateAsync(administrator.Client, "it-fresh-unlock-target");
        var target = await ProfileHttp.AccountAsync(vault, "it-fresh-unlock-target");
        using (var setup = vault.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
            await db.Users.Where(account => account.Id == target.Id)
                .ExecuteUpdateAsync(change => change.SetProperty(account => account.LockedUntilUtc, DateTimeOffset.UtcNow.AddMinutes(15)));
        }
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, administrator, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("198.18.109.2"));

        var response = await UserActions.UnlockAsync(stale.Client, target.Id);

        Assert.NotNull((await ProfileHttp.AccountAsync(vault, "it-fresh-unlock-target")).LockedUntilUtc);
        await AssertStepUpAsync(response, "code");
    }

    [Fact]
    public async Task A_stale_administrator_cannot_reset_another_accounts_authenticator()
    {
        var administrator = await vault.SignInNewAccountAsync("it-fresh-reset-admin", IPAddress.Parse("198.18.109.3"), ClearanceLevel.TopSecret);
        await UserAccounts.CreateAsync(administrator.Client, "it-fresh-reset-target");
        var target = await ProfileHttp.AccountAsync(vault, "it-fresh-reset-target");
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, administrator, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("198.18.109.3"));

        var response = await UserActions.ResetMfaAsync(stale.Client, target.Id);

        Assert.Equal(target.CredentialGeneration, (await ProfileHttp.AccountAsync(vault, "it-fresh-reset-target")).CredentialGeneration);
        await AssertStepUpAsync(response, "code");
    }
    internal static async Task AssertStepUpAsync(HttpResponseMessage response, string proof)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["code", "proof", "status", "title"], body.RootElement.EnumerateObject().Select(member => member.Name).Order());
        Assert.Equal(403, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("step_up_required", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("Confirm it's you before granting access.", body.RootElement.GetProperty("title").GetString());
        Assert.Equal(proof, body.RootElement.GetProperty("proof").GetString());
    }
}
