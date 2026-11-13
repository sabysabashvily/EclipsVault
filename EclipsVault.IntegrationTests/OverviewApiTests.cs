using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models.Ui;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The Overview describes only the secrets its caller could open now, and only an administrator gets
/// the vault's activity and its account count. A trusted address is in 10.0.0.0/8; 100.163.0.0/16 is
/// not. Each account signs in from its own address, so no test spends another's sign-in budget.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class OverviewApiTests(VaultFixture vault)
{
    [Fact]
    public async Task The_overview_refuses_anonymous_and_working_api_key_callers()
    {
        var client = vault.CreateClient();
        await UiApiFailureContractTests.AssertProblemAsync(await client.GetAsync("/api/ui/overview"), 401, "unauthenticated");
        var key = await vault.IssueApiKeyAsync(await vault.AdminClientAsync(), "it-overview-key");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/secrets")).StatusCode);

        await UiApiFailureContractTests.AssertProblemAsync(await client.GetAsync("/api/ui/overview"), 401, "unauthenticated");
    }

    [Fact]
    public async Task An_administrator_gets_every_field_the_staff_count_and_the_vaults_activity()
    {
        var admin = await vault.AdminClientAsync();
        // Sooner than any other test's secret, so it is among the vault's eight soonest.
        var expiring = await OverviewSecrets.CreateAsync(vault, "it-overview-shape-expiring", "GLOBAL", expiresInDays: 0.01);

        var response = await admin.GetAsync("/api/ui/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var overview = document.RootElement;
        Assert.Equal(
            ["activityScope", "criticalEventsLast24h", "developmentCount", "displayName", "expiringSoon", "expiringWithin7Days",
             "productionCount", "recentEvents", "staffAccounts", "stagingCount", "totalActiveSecrets"],
            NamesOf(overview));
        Assert.Equal("vault", overview.GetProperty("activityScope").GetString());
        Assert.True(overview.GetProperty("staffAccounts").GetInt32() > 0);
        var events = overview.GetProperty("recentEvents").EnumerateArray().ToArray();
        Assert.NotEmpty(events);
        Assert.All(events, entry => Assert.Equal(
            ["action", "actionLabel", "actor", "isCritical", "resource", "timestampUtc", "tone"], NamesOf(entry)));
        var listed = overview.GetProperty("expiringSoon").EnumerateArray()
            .Single(secret => secret.GetProperty("secretId").GetGuid() == expiring.Id);
        Assert.Equal(["environment", "expiresAtUtc", "name", "secretId"], NamesOf(listed));
        Assert.Equal("it-overview-shape-expiring", listed.GetProperty("name").GetString());
        Assert.Equal("Development", listed.GetProperty("environment").GetString());
        Assert.Equal(expiring.ExpiresAtUtc!.Value.UtcDateTime, listed.GetProperty("expiresAtUtc").GetDateTime(), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task A_non_administrator_gets_no_staff_count_and_only_their_own_activity()
    {
        var member = await vault.SignInNewAccountAsync("it-overview-member", IPAddress.Parse("10.163.0.1"), project: "IT-OVERVIEW-MEMBER");
        // The vault's newest row is now the administrator's, not the member's.
        await OverviewSecrets.CreateAsync(vault, "it-overview-member-elsewhere", "GLOBAL");

        var overview = await member.Client.GetFromJsonAsync<OverviewResponse>("/api/ui/overview");

        Assert.Null(overview!.StaffAccounts);
        Assert.Equal("own", overview.ActivityScope);
        Assert.NotEmpty(overview.RecentEvents);
        Assert.All(overview.RecentEvents, entry => Assert.Equal("it-overview-member", entry.Actor));
    }

    [Fact]
    public async Task The_figures_leave_out_another_projects_an_under_cleared_and_a_decoy_secret()
    {
        const string project = "IT-OVERVIEW-FILTER";
        await OverviewSecrets.CreateAsync(vault, "it-overview-filter-visible", project, expiresInDays: 2);
        await OverviewSecrets.CreateAsync(vault, "it-overview-filter-confidential", project, sensitivity: "Confidential", environment: "Staging");
        await OverviewSecrets.CreateAsync(vault, "it-overview-filter-other-project", "IT-OVERVIEW-ELSEWHERE", expiresInDays: 2);
        await OverviewSecrets.CreateAsync(vault, "it-overview-filter-under-cleared", project, sensitivity: "Secret", expiresInDays: 2);
        await OverviewSecrets.CreateAsync(vault, "it-overview-filter-decoy", project, expiresInDays: 2, decoy: true);
        var member = await vault.SignInNewAccountAsync("it-overview-filter", IPAddress.Parse("10.163.0.2"), ClearanceLevel.Elevated, project);

        var overview = await member.Client.GetFromJsonAsync<OverviewResponse>("/api/ui/overview");

        Assert.Equal(2, overview!.TotalActiveSecrets);
        Assert.Equal(1, overview.DevelopmentCount);
        Assert.Equal(1, overview.StagingCount);
        Assert.Equal(0, overview.ProductionCount);
        Assert.Equal(1, overview.ExpiringWithin7Days);
        Assert.Equal(["it-overview-filter-visible"], overview.ExpiringSoon.Select(secret => secret.Name));
    }

    [Fact]
    public async Task The_figures_leave_out_a_secret_that_needs_a_trusted_network_off_the_network()
    {
        const string project = "IT-OVERVIEW-NETWORK";
        await OverviewSecrets.CreateAsync(vault, "it-overview-network-internal", project, expiresInDays: 2);
        await OverviewSecrets.CreateAsync(vault, "it-overview-network-confidential", project, sensitivity: "Confidential", expiresInDays: 2);
        var member = await vault.SignInNewAccountAsync("it-overview-network", IPAddress.Parse("100.163.0.1"), ClearanceLevel.Elevated, project);

        var overview = await member.Client.GetFromJsonAsync<OverviewResponse>("/api/ui/overview");

        Assert.Equal(1, overview!.TotalActiveSecrets);
        Assert.Equal(1, overview.ExpiringWithin7Days);
        Assert.Equal(["it-overview-network-internal"], overview.ExpiringSoon.Select(secret => secret.Name));
    }

    [Fact]
    public async Task The_expiring_count_covers_every_expiring_secret_while_the_list_stops_at_eight()
    {
        const string project = "IT-OVERVIEW-NINE";
        for (var hours = 1; hours <= 9; hours++)
        {
            await OverviewSecrets.CreateAsync(vault, $"it-overview-nine-{hours}", project, expiresInDays: hours / 24.0);
        }
        var member = await vault.SignInNewAccountAsync("it-overview-nine", IPAddress.Parse("10.163.0.3"), project: project);

        var overview = await member.Client.GetFromJsonAsync<OverviewResponse>("/api/ui/overview");

        Assert.Equal(9, overview!.ExpiringWithin7Days);
        Assert.Equal(
            ["it-overview-nine-1", "it-overview-nine-2", "it-overview-nine-3", "it-overview-nine-4",
             "it-overview-nine-5", "it-overview-nine-6", "it-overview-nine-7", "it-overview-nine-8"],
            overview.ExpiringSoon.Select(secret => secret.Name));
    }

    [Fact]
    public async Task A_new_account_with_a_deleted_accounts_username_does_not_get_its_activity()
    {
        var deleted = await vault.SignInNewAccountAsync("it-overview-reused", IPAddress.Parse("10.163.0.7"));
        await WriteRevealAsync(SessionApi.PrincipalOf(vault, deleted).GetUserId(), "it-overview-reused", "it-overview-reused-old-secret");
        await vault.DeleteAccountAsync("it-overview-reused");
        var successor = await vault.SignInNewAccountAsync("it-overview-reused", IPAddress.Parse("10.163.0.8"));

        var overview = await successor.Client.GetFromJsonAsync<OverviewResponse>("/api/ui/overview");

        Assert.NotEmpty(overview!.RecentEvents);
        Assert.DoesNotContain(overview.RecentEvents, entry => entry.Resource == "it-overview-reused-old-secret");
    }

    [Fact]
    public async Task Reading_the_overview_writes_no_audit_row()
    {
        var member = await vault.SignInNewAccountAsync("it-overview-unaudited", IPAddress.Parse("10.163.0.4"));
        var before = await AuditTrail.HeadAsync(vault);

        Assert.Equal(HttpStatusCode.OK, (await member.Client.GetAsync("/api/ui/overview")).StatusCode);

        Assert.DoesNotContain(await AuditTrail.RowsAfterAsync(vault, before), row => row.Username == "it-overview-unaudited");
    }

    [Theory]
    [InlineData("/", "it-overview-root", "10.163.0.11")]
    [InlineData("/Home", "it-overview-home", "10.163.0.12")]
    [InlineData("/hOmE/InDeX", "it-overview-home-index", "10.163.0.13")]
    public async Task Signed_in_the_overview_paths_serve_the_Angular_document(string path, string username, string source)
    {
        var member = await vault.SignInNewAccountAsync(username, IPAddress.Parse(source));

        var document = await member.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.True(document.Headers.CacheControl?.NoStore);
        Assert.Contains("<app-root>", await document.Content.ReadAsStringAsync());
        Assert.Contains("require-trusted-types-for 'script'", Assert.Single(document.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task Signed_out_the_root_serves_the_landing_page()
    {
        var landing = await vault.CreateClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, landing.StatusCode);
        var html = await landing.Content.ReadAsStringAsync();
        Assert.Contains("Secrets, kept in the shadow.", html);
        Assert.DoesNotContain("<app-root>", html);
    }

    [Fact]
    public async Task A_denial_names_its_secret_to_administrators_and_never_in_the_callers_own_views()
    {
        var member = await vault.SignInNewAccountAsync("it-overview-denied", IPAddress.Parse("10.163.0.5"));
        await WriteDenialAsync(SessionApi.PrincipalOf(vault, member).GetUserId(), "it-overview-denied", "it-overview-denied-secret");
        var admin = await vault.AdminClientAsync();

        var vaultActivity = await admin.GetFromJsonAsync<OverviewResponse>("/api/ui/overview");
        var auditLog = await admin.GetFromJsonAsync<AuditResponse>("/api/ui/audit");
        var ownOverview = await member.Client.GetFromJsonAsync<OverviewResponse>("/api/ui/overview");
        var myActivity = await member.Client.GetFromJsonAsync<ActivityResponse>("/api/ui/activity");
        var export = await UiApiRequest.WriteWithAntiforgeryAsync(member, HttpMethod.Post, "/api/ui/data/export");

        var administratorRow = vaultActivity!.RecentEvents.Single(entry => entry.Actor == "it-overview-denied" && entry.Action == "SecretAccessDenied");
        Assert.Equal(("Access denied", "warn", "it-overview-denied-secret"), (administratorRow.ActionLabel, administratorRow.Tone, administratorRow.Resource));
        var auditRow = auditLog!.Entries.Single(entry => entry.Actor == "it-overview-denied" && entry.Action == "SecretAccessDenied");
        Assert.Equal(("Access denied", "it-overview-denied-secret"), (auditRow.ActionLabel, auditRow.Resource));
        var ownRow = Assert.Single(ownOverview!.RecentEvents, entry => entry.Action == "SecretAccessDenied");
        Assert.Equal(("Access denied", "Secret"), (ownRow.ActionLabel, ownRow.Resource));
        var activityRow = Assert.Single(myActivity!.Items, item => item.Title == "Was refused access to a secret");
        Assert.Null(activityRow.ResourceName);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var exportText = await export.Content.ReadAsStringAsync();
        Assert.DoesNotContain("it-overview-denied-secret", exportText);
        using var exported = JsonDocument.Parse(exportText);
        var exportRow = Assert.Single(exported.RootElement.GetProperty("RecentActivity").EnumerateArray(),
            entry => entry.GetProperty("Action").GetString() == "Was refused access to a secret");
        Assert.Equal(JsonValueKind.Null, exportRow.GetProperty("Resource").ValueKind);
    }

    /// <summary>A refused by-id action's row, through the vault's own fail-closed sink, as 7b and 7c write it.</summary>
    private async Task WriteDenialAsync(Guid accountId, string username, string secretName)
    {
        using var scope = vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditSink>().WriteAsync(new AuditEntry
        {
            Action = AuditAction.SecretAccessDenied,
            ResourceType = "Secret",
            ResourceId = Guid.NewGuid(),
            ResourceName = secretName,
            Details = "Clearance 'Standard' is below required sensitivity 'Secret'.",
            ActorUserId = accountId,
            ActorUsername = username,
        }, CancellationToken.None);
    }

    /// <summary>A reveal by the account, through the vault's own fail-closed sink.</summary>
    private async Task WriteRevealAsync(Guid accountId, string username, string secretName)
    {
        using var scope = vault.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditSink>().WriteAsync(new AuditEntry
        {
            Action = AuditAction.SecretRevealed,
            ResourceType = "Secret",
            ResourceName = secretName,
            ActorUserId = accountId,
            ActorUsername = username,
        }, CancellationToken.None);
    }

    private static string[] NamesOf(JsonElement element)
        => element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
}

/// <summary>Production secrets open only inside the production window, so outside it the Overview leaves them out.</summary>
[Collection(ClosedProductionWindowCollection.Name)]
public sealed class OverviewProductionWindowTests(ClosedProductionWindowFixture fixture)
{
    [Fact]
    public async Task The_figures_leave_out_a_production_secret_while_the_window_is_closed()
    {
        var vault = fixture.Vault;
        const string project = "IT-OVERVIEW-WINDOW";
        await OverviewSecrets.CreateAsync(vault, "it-overview-window-production", project, environment: "Production", expiresInDays: 2);
        await OverviewSecrets.CreateAsync(vault, "it-overview-window-development", project, expiresInDays: 2);
        var member = await vault.SignInNewAccountAsync("it-overview-window", IPAddress.Parse("10.163.0.6"), project: project);

        var overview = await member.Client.GetFromJsonAsync<OverviewResponse>("/api/ui/overview");

        Assert.Equal(1, overview!.TotalActiveSecrets);
        Assert.Equal(0, overview.ProductionCount);
        Assert.Equal(["it-overview-window-development"], overview.ExpiringSoon.Select(secret => secret.Name));
    }
}

/// <summary>
/// Secrets for Overview tests. The administrator creates each one through the Legacy form, which files
/// it under GLOBAL with no expiry; its project, expiry and decoy flag are then set in the database,
/// as an operator flags a decoy. The Overview reads metadata only, so no value is ever decrypted.
/// </summary>
internal static class OverviewSecrets
{
    public static async Task<Core.Domain.Entities.Secret> CreateAsync(
        VaultFixture vault, string name, string project, string sensitivity = "Internal", string environment = "Development",
        double? expiresInDays = null, bool decoy = false)
    {
        var details = await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), name, sensitivity: sensitivity, environment: environment);
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        var secret = await db.Secrets.SingleAsync(row => row.Id == LegacySharing.SecretIdOf(details));
        secret.ProjectKey = project;
        secret.ExpiresAtUtc = expiresInDays is { } days ? DateTimeOffset.UtcNow.AddDays(days) : null;
        secret.IsHoneyToken = decoy;
        await db.SaveChangesAsync();
        return secret;
    }
}
