using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A secret made a decoy after it has an archived version, a share and a pending access request: every
/// by-id endpoint and both of the Denied page's questions trip it, and no list shows it.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class DecoyMatrixTests(VaultFixture vault)
{
    private const string Elsewhere = "IT-DECOY-ELSEWHERE";

    [Theory]
    [InlineData("get", "100.97.1.10", "10.173.1.1")]
    [InlineData("reveal", "100.97.2.10", "10.173.2.1")]
    [InlineData("reveal-version", "100.97.3.10", "10.173.3.1")]
    [InlineData("rotate", "100.97.4.10", "10.173.4.1")]
    [InlineData("rotate-managed", "100.97.5.10", "10.173.5.1")]
    [InlineData("restore", "100.97.6.10", "10.173.6.1")]
    [InlineData("share", "100.97.7.10", "10.173.7.1")]
    [InlineData("revoke", "100.97.8.10", "10.173.8.1")]
    [InlineData("delete", "100.97.9.10", "10.173.9.1")]
    [InlineData("eligibility", "100.97.10.10", "10.173.10.1")]
    [InlineData("filing", "100.97.11.10", "10.173.11.1")]
    public async Task Every_endpoint_trips_a_decoy_that_has_history_and_answers_as_for_an_unknown_id(
        string endpoint, string address, string askerAddress)
    {
        // Tripping ends the caller's sessions and blocks its address, so each row signs in its own caller.
        await UserAccounts.CreateAsync(await vault.AdminClientAsync(), $"it-decoy-{endpoint}-holder", project: Elsewhere);
        var decoy = await DecoyWithHistoryAsync($"it-decoy-{endpoint}", $"it-decoy-{endpoint}-holder");
        var caller = (await vault.SignInNewAccountAsync($"it-decoy-{endpoint}-caller", IPAddress.Parse(address))).Client;
        var asker = (await vault.SignInNewAccountAsync($"it-decoy-{endpoint}-asker", IPAddress.Parse(askerAddress))).Client;
        var unknown = await SendAsync(endpoint, asker, $"/Secrets/Details/{Guid.NewGuid()}", decoy);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await SendAsync(endpoint, caller, decoy.Details, decoy);

        await SecretsApi.AssertSameAnswerAsync(unknown, response);
        Assert.Equal([AuditAction.HoneyTokenTripped], (await AuditTrail.RowsAfterAsync(vault, before)).Select(row => row.Action));
        Assert.True(await SecretDetailsApi.ExistsAsync(vault, decoy.Details));
        Assert.Equal(1, await SecretDetailsApi.VersionCountAsync(vault, decoy.Details));
    }

    [Fact]
    public async Task No_list_shows_a_decoy_that_has_history()
    {
        var holder = await vault.SignInNewAccountAsync("it-decoy-lists-holder", IPAddress.Parse("10.173.20.1"), project: Elsewhere);
        var decoy = await DecoyWithHistoryAsync("it-decoy-lists", "it-decoy-lists-holder");
        var admin = await vault.AdminClientAsync();

        Assert.DoesNotContain(decoy.Name, await admin.GetStringAsync("/api/ui/secrets"));
        Assert.DoesNotContain(decoy.Name, await admin.GetStringAsync("/api/ui/secrets/search?q=it-decoy-lists"));
        // The Overview's recent events are the audit trail, which names decoys to administrators (ADR-0028).
        // Its expiring list shows only the soonest few, so a member of the decoy's own project reads it.
        const string project = "IT-DECOY-LISTS";
        using (var scope = vault.Services.CreateScope())
        {
            var secretId = SecretSharing.SecretIdOf(decoy.Details);
            await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Secrets.Where(secret => secret.Id == secretId)
                .ExecuteUpdateAsync(set => set.SetProperty(secret => secret.ProjectKey, project));
        }

        var member = await vault.SignInNewAccountAsync("it-decoy-lists-member", IPAddress.Parse("10.173.21.1"), project: project);
        var overview = await member.Client.GetFromJsonAsync<JsonElement>("/api/ui/overview");
        Assert.Empty(overview.GetProperty("expiringSoon").EnumerateArray());
        Assert.DoesNotContain(decoy.Name, await admin.GetStringAsync("/api/ui/shared-by-me"));
        Assert.DoesNotContain(decoy.Name, await AccessRequestsApi.ToReviewAsync(admin));
        Assert.DoesNotContain(decoy.Name, await holder.Client.GetStringAsync("/api/ui/shared-with-me"));
    }

    /// <summary>Calls one endpoint about <paramref name="details"/>, with the decoy's version, share and holder.</summary>
    private static async Task<HttpResponseMessage> SendAsync(string endpoint, HttpClient client, string details, Decoy decoy)
        => endpoint switch
        {
            "get" => await SecretDetailsApi.GetAsync(client, details),
            "reveal" => await SecretDetailsApi.RevealAsync(client, details),
            "reveal-version" => await SecretDetailsApi.RevealVersionAsync(client, details, decoy.Version),
            "rotate" => await SecretDetailsApi.RotateAsync(client, details, "it-decoy-new-value"),
            "rotate-managed" => await SecretDetailsApi.RotateUpstreamAsync(client, details),
            "restore" => await SecretDetailsApi.RestoreAsync(client, details, decoy.Version),
            "share" => await SecretDetailsApi.ShareAsync(client, details, decoy.Holder),
            "revoke" => await SecretDetailsApi.RevokeAsync(client, details, decoy.Share),
            "delete" => await SecretDetailsApi.DeleteAsync(client, details),
            "eligibility" => await AccessRequestsApi.EligibilityAsync(client, SecretSharing.SecretIdOf(details)),
            "filing" => await AccessRequestsApi.FileAsync(client, SecretSharing.SecretIdOf(details)),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint)),
        };

    private sealed record Decoy(string Name, string Details, Guid Version, Guid Share, string Holder);

    /// <summary>A decoy with an archived version, a share to <paramref name="holder"/> and a pending request.</summary>
    private async Task<Decoy> DecoyWithHistoryAsync(string name, string holder)
    {
        var admin = await vault.AdminClientAsync();
        // It expires soon, so the Overview's expiring list would show it were decoys not left out.
        var details = await SecretSharing.CreateSecretAsync(admin, name, value: "it-decoy-first-value", ttlDays: 3);
        SecretDetailsApi.AssertNoContent(await SecretDetailsApi.RotateAsync(admin, details, "it-decoy-live-value"));
        await SecretSharing.InsertShareAsync(vault, details, holder);
        await UserAccounts.CreateAsync(admin, $"{name}-requester", project: Elsewhere);
        await AccessRequestsApi.PendingAsync(vault, details, $"{name}-requester");
        var version = await SecretDetailsApi.NewestVersionAsync(vault, details);
        var share = (await SecretSharing.ShareOfAsync(vault, details)).Id;
        await SecretSharing.MakeDecoyAsync(vault, details);
        return new Decoy(name, details, version, share, holder);
    }
}
