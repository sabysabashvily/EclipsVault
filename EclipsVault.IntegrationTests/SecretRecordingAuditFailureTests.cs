using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A secret action whose audit row cannot be written discloses and changes nothing. Each test sets up
/// and signs in with auditing working, then rejects every audit write for the step under test.
/// </summary>
[Collection(AuditFailureCollection.Name)]
public sealed class SecretRecordingAuditFailureTests(AuditFailureFixture failure)
{
    private const string AuditOutage = "/Home/Error?code=503";

    [Fact]
    public async Task A_details_page_whose_view_cannot_be_recorded_is_not_served()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var details = await LegacySharing.CreateSecretAsync(admin, "it-failure-unrecorded-view");

        await failure.RejectAuditWritesAsync();
        try
        {
            LegacySecretActions.AssertRedirectedTo(await admin.GetAsync(details), AuditOutage);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_refused_details_page_that_cannot_be_recorded_answers_as_an_audit_outage()
    {
        var vault = failure.Vault;
        var details = await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-failure-unrecorded-page-denial", sensitivity: "Secret");
        var member = await vault.SignInNewAccountAsync("it-failure-unrecorded-page-denial", IPAddress.Parse("10.167.0.1"));

        await failure.RejectAuditWritesAsync();
        try
        {
            LegacySecretActions.AssertRedirectedTo(await member.Client.GetAsync(details), AuditOutage);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_refused_public_api_read_that_cannot_be_recorded_answers_as_an_audit_outage_without_its_reasons()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var details = await LegacySharing.CreateSecretAsync(admin, "it-failure-unrecorded-api-denial", sensitivity: "Secret");
        var service = vault.CreateSession(source: IPAddress.Parse("10.167.0.2")).Client;
        service.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await vault.IssueApiKeyAsync(admin, "it-failure-unrecorded-api-denial"));

        await failure.RejectAuditWritesAsync();
        try
        {
            var refused = await service.GetAsync($"/api/v1/secrets/{LegacySharing.SecretIdOf(details)}");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
            Assert.Equal("""{"error":"audit_unavailable"}""", await refused.Content.ReadAsStringAsync());
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    [Fact]
    public async Task A_restore_that_cannot_be_recorded_changes_nothing()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var details = await LegacySharing.CreateSecretAsync(admin, "it-failure-unrecorded-restore", value: "it-failure-first-value");
        var token = await LegacySecretActions.FormTokenAsync(admin, details);
        Assert.Equal(HttpStatusCode.Redirect, (await LegacySecretActions.RotateAsync(admin, details, token, "it-failure-second-value")).StatusCode);
        var version = await LegacySecretActions.NewestVersionAsync(vault, details);

        await failure.RejectAuditWritesAsync();
        try
        {
            LegacySecretActions.AssertRedirectedTo(await LegacySecretActions.RestoreAsync(admin, details, token, version), AuditOutage);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        Assert.Equal("it-failure-second-value", await UpstreamRotationTests.StoredValueAsync(vault, details));
        Assert.Equal(version, await LegacySecretActions.NewestVersionAsync(vault, details));
    }

    [Fact]
    public async Task An_upstream_rotation_that_cannot_be_recorded_puts_the_login_back_and_changes_nothing()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var password = ManagedTargetLogins.NewPassword();
        await ManagedTargetLogins.CreateAsync(vault, "it_failure_upstream", password);
        var details = await LegacySharing.CreateSecretAsync(admin, "it-failure-unrecorded-upstream", value: password);
        await LegacySecretActions.MakeManagedAsync(vault, details, "it_failure_upstream");
        var token = await LegacySecretActions.FormTokenAsync(admin, details);

        await failure.RejectAuditWritesAsync();
        try
        {
            LegacySecretActions.AssertRedirectedTo(await LegacySecretActions.RotateUpstreamAsync(admin, details, token), AuditOutage);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        Assert.True(await ManagedTargetLogins.OpensWithAsync(vault, "it_failure_upstream", password));
        Assert.Equal(password, await UpstreamRotationTests.StoredValueAsync(vault, details));
    }
}
