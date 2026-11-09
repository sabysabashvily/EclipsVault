using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class LicenseApiTests(VaultFixture vault)
{
    [Theory]
    [InlineData("/api/ui/notifications", "198.18.115.11")]
    [InlineData("/api/ui/license", "198.18.115.12")]
    public async Task Monitoring_accepts_only_an_administrator_session(string path, string source)
    {
        var anonymous = vault.CreateClient();
        var reader = await vault.SignInNewAccountAsync("it-monitor-auth-" + source.Split('.').Last(), IPAddress.Parse(source));
        var key = vault.CreateClient();
        key.DefaultRequestHeaders.Authorization = new("Bearer", await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), "it-monitor-key-" + source.Split('.').Last()));

        await UiApiFailureContractTests.AssertProblemAsync(await anonymous.GetAsync(path), 401, "unauthenticated");
        await UiApiFailureContractTests.AssertProblemAsync(await reader.Client.GetAsync(path), 403, "forbidden");
        await UiApiFailureContractTests.AssertProblemAsync(await key.GetAsync(path), 401, "unauthenticated");
    }

    [Fact]
    public async Task An_unlicensed_vault_reports_its_startup_snapshot_without_starting_an_evaluation()
    {
        using var scope = vault.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        Assert.Empty(await database.FeatureEvaluations.ToListAsync());

        var response = await (await vault.AdminClientAsync()).GetAsync("/api/ui/license");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["beyondTier", "licence", "message", "status", "updatesLapsed"], ProfileApi.NamesOf(body));
        Assert.Equal("Missing", body.GetProperty("status").GetString());
        Assert.Equal("No license is configured — running unlicensed.", body.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("licence").ValueKind);
        Assert.False(body.GetProperty("updatesLapsed").GetBoolean());
        Assert.Equal(["sso"], body.GetProperty("beyondTier").EnumerateArray().Select(feature => feature.GetString()!).ToArray());
        Assert.Empty(await database.FeatureEvaluations.ToListAsync());
    }
}

[Collection(ClosedProductionWindowCollection.Name)]
public sealed class InvalidLicenseApiTests(ClosedProductionWindowFixture fixture)
{
    [Fact]
    public async Task A_malformed_licence_answers_normally_without_starting_an_evaluation()
    {
        using var scope = fixture.Vault.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        Assert.Empty(await database.FeatureEvaluations.ToListAsync());

        var response = await (await fixture.Vault.AdminClientAsync()).GetAsync("/api/ui/license");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Malformed", body.GetProperty("status").GetString());
        Assert.Equal("The license is not a readable EclipsVault token.", body.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("licence").ValueKind);
        Assert.False(body.GetProperty("updatesLapsed").GetBoolean());
        Assert.Empty(await database.FeatureEvaluations.ToListAsync());
    }
}
