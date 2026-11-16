using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Models.Ui;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A secret action records what happened: a refusal as a refusal, a served page as a view, and every
/// other action as its own single event. A trusted address is in 10.0.0.0/8, and each account signs in
/// from its own address so no test spends another's sign-in budget.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class SecretRecordingTests(VaultFixture vault)
{
    private const string ClearanceReason = "Clearance 'Standard' is below required sensitivity 'Secret'.";

    [Fact]
    public async Task A_refused_details_page_records_one_access_denial_and_no_view()
    {
        var details = await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-recording-refused-page", sensitivity: "Secret");
        var member = await vault.SignInNewAccountAsync("it-recording-refused-page", IPAddress.Parse("10.166.0.1"));
        var before = await AuditTrail.HeadAsync(vault);

        var response = await member.Client.GetAsync(details);

        LegacySecretActions.AssertRedirectedTo(response, $"/Account/Denied?secretId={LegacySharing.SecretIdOf(details)}");
        var denial = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before), row => row.ResourceId == LegacySharing.SecretIdOf(details));
        Assert.Equal(
            (AuditAction.SecretAccessDenied, "it-recording-refused-page", "it-recording-refused-page", false),
            (denial.Action, denial.Username, denial.ResourceName, denial.IsCritical));
        Assert.Contains(ClearanceReason, denial.Details);
    }

    [Fact]
    public async Task A_refused_public_api_read_records_one_access_denial_and_no_view()
    {
        var admin = await vault.AdminClientAsync();
        var details = await LegacySharing.CreateSecretAsync(admin, "it-recording-refused-api", sensitivity: "Secret");
        var service = vault.CreateSession(source: IPAddress.Parse("10.166.0.2")).Client;
        service.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await vault.IssueApiKeyAsync(admin, "it-recording-refused-api"));
        var before = await AuditTrail.HeadAsync(vault);

        var response = await service.GetAsync($"/api/v1/secrets/{LegacySharing.SecretIdOf(details)}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(ClearanceReason, await response.Content.ReadAsStringAsync());
        var denial = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before), row => row.ResourceId == LegacySharing.SecretIdOf(details));
        Assert.Equal((AuditAction.SecretAccessDenied, "it-recording-refused-api", false), (denial.Action, denial.Username, denial.IsCritical));
        Assert.Contains(ClearanceReason, denial.Details);
    }

    [Fact]
    public async Task A_refusal_appears_in_the_refused_accounts_own_overview_without_the_secrets_name()
    {
        var details = await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-recording-unnamed-overview-secret", sensitivity: "Secret");
        var member = await vault.SignInNewAccountAsync("it-recording-unnamed-overview", IPAddress.Parse("10.166.0.3"));
        Assert.Equal(HttpStatusCode.Redirect, (await member.Client.GetAsync(details)).StatusCode);

        var ownOverview = await member.Client.GetFromJsonAsync<OverviewResponse>("/api/ui/overview");

        var ownRow = Assert.Single(ownOverview!.RecentEvents, entry => entry.Action == "SecretAccessDenied");
        Assert.Equal("Secret", ownRow.Resource);
    }

    [Fact]
    public async Task A_refusal_appears_in_the_refused_accounts_own_activity_without_the_secrets_name()
    {
        var details = await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-recording-unnamed-activity-secret", sensitivity: "Secret");
        var member = await vault.SignInNewAccountAsync("it-recording-unnamed-activity", IPAddress.Parse("10.166.0.6"));
        Assert.Equal(HttpStatusCode.Redirect, (await member.Client.GetAsync(details)).StatusCode);

        var myActivity = await member.Client.GetFromJsonAsync<ActivityResponse>("/api/ui/activity");

        var activityRow = Assert.Single(myActivity!.Items, item => item.Title == "Was refused access to a secret");
        Assert.Null(activityRow.ResourceName);
    }

    [Fact]
    public async Task A_refusal_appears_in_the_refused_accounts_data_export_without_the_secrets_name()
    {
        var details = await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-recording-unnamed-export-secret", sensitivity: "Secret");
        var member = await vault.SignInNewAccountAsync("it-recording-unnamed-export", IPAddress.Parse("10.166.0.7"));
        Assert.Equal(HttpStatusCode.Redirect, (await member.Client.GetAsync(details)).StatusCode);

        var export = await UiApiRequest.WriteWithAntiforgeryAsync(member, HttpMethod.Post, "/api/ui/data/export");

        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var exportText = await export.Content.ReadAsStringAsync();
        Assert.DoesNotContain("it-recording-unnamed-export-secret", exportText);
        using var exported = JsonDocument.Parse(exportText);
        Assert.Single(exported.RootElement.GetProperty("RecentActivity").EnumerateArray(),
            entry => entry.GetProperty("Action").GetString() == "Was refused access to a secret");
    }

    [Fact]
    public async Task An_allowed_details_page_records_one_view()
    {
        var admin = await vault.AdminClientAsync();
        var details = await LegacySharing.CreateSecretAsync(admin, "it-recording-viewed");
        var before = await AuditTrail.HeadAsync(vault);

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(details)).StatusCode);

        Assert.Equal([AuditAction.SecretMetadataViewed], await LegacySecretActions.ActionsAfterAsync(vault, before, details));
    }

    [Fact]
    public async Task A_reveal_records_only_the_reveal()
    {
        var admin = await vault.AdminClientAsync();
        var details = await LegacySharing.CreateSecretAsync(admin, "it-recording-revealed");
        var token = await LegacySecretActions.FormTokenAsync(admin, details);
        var before = await AuditTrail.HeadAsync(vault);

        Assert.Equal(HttpStatusCode.OK, (await LegacySecretActions.RevealAsync(admin, details, token)).StatusCode);

        Assert.Equal([AuditAction.SecretRevealed], await LegacySecretActions.ActionsAfterAsync(vault, before, details));
    }

    [Fact]
    public async Task A_public_api_read_records_only_the_reveal()
    {
        var admin = await vault.AdminClientAsync();
        var details = await LegacySharing.CreateSecretAsync(admin, "it-recording-api-revealed", value: "it-recording-api-value");
        var service = vault.CreateSession(source: IPAddress.Parse("10.166.0.4")).Client;
        service.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await vault.IssueApiKeyAsync(admin, "it-recording-api-revealed"));
        var before = await AuditTrail.HeadAsync(vault);

        var response = await service.GetAsync($"/api/v1/secrets/{LegacySharing.SecretIdOf(details)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("it-recording-api-value", await response.Content.ReadAsStringAsync());
        Assert.Equal([AuditAction.SecretRevealed], await LegacySecretActions.ActionsAfterAsync(vault, before, details));
    }

    [Fact]
    public async Task A_stale_session_asked_to_step_up_before_a_reveal_records_nothing_for_the_secret()
    {
        var admin = await vault.AdminClientAsync();
        var details = await LegacySharing.CreateSecretAsync(admin, "it-recording-step-up", sensitivity: "Secret");
        var member = await vault.SignInNewAccountAsync("it-recording-step-up", IPAddress.Parse("10.166.0.5"), ClearanceLevel.Secret);
        var stale = vault.CreateSession(
            SessionApi.WithStrongAuthenticationAt(vault, member, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("10.166.0.5"));
        var token = await LegacySecretActions.FormTokenAsync(stale.Client, details);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacySecretActions.RevealAsync(stale.Client, details, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("stepup-code", await response.Content.ReadAsStringAsync());
        Assert.Empty(await LegacySecretActions.ActionsAfterAsync(vault, before, details));
    }

    [Fact]
    public async Task A_restore_records_only_the_restore()
    {
        var admin = await vault.AdminClientAsync();
        var details = await LegacySharing.CreateSecretAsync(admin, "it-recording-restored");
        var token = await LegacySecretActions.FormTokenAsync(admin, details);
        Assert.Equal(HttpStatusCode.Redirect, (await LegacySecretActions.RotateAsync(admin, details, token, "it-recording-second-value")).StatusCode);
        var version = await LegacySecretActions.NewestVersionAsync(vault, details);
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacySecretActions.RestoreAsync(admin, details, token, version);

        LegacySecretActions.AssertRedirectedTo(response, details);
        Assert.Equal([AuditAction.SecretVersionRestored], await LegacySecretActions.ActionsAfterAsync(vault, before, details));
    }
}
