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
    [Fact]
    public async Task A_details_page_whose_view_cannot_be_recorded_is_not_served()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-failure-unrecorded-view");

        var response = await WithAuditWritesRejectedAsync(() => SecretDetailsApi.GetAsync(admin, details));

        await AssertAuditOutageAsync(response);
        Assert.DoesNotContain("it-failure-unrecorded-view", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_refused_details_page_that_cannot_be_recorded_answers_as_an_audit_outage_without_its_reasons()
    {
        var vault = failure.Vault;
        var details = await SecretSharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-failure-unrecorded-page-denial", sensitivity: "Secret");
        var member = await vault.SignInNewAccountAsync("it-failure-unrecorded-page-denial", IPAddress.Parse("10.167.0.1"));

        var response = await WithAuditWritesRejectedAsync(() => SecretDetailsApi.GetAsync(member.Client, details));

        await AssertAuditOutageAsync(response);
        Assert.DoesNotContain("Clearance", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_refused_public_api_read_that_cannot_be_recorded_answers_as_an_audit_outage_without_its_reasons()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-failure-unrecorded-api-denial", sensitivity: "Secret");
        var service = vault.CreateSession(source: IPAddress.Parse("10.167.0.2")).Client;
        service.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await vault.IssueApiKeyAsync(admin, "it-failure-unrecorded-api-denial"));

        var refused = await WithAuditWritesRejectedAsync(() => service.GetAsync($"/api/v1/secrets/{SecretSharing.SecretIdOf(details)}"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Equal("""{"error":"audit_unavailable"}""", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_reveal_that_cannot_be_recorded_discloses_nothing()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-failure-unrecorded-reveal", value: "it-failure-reveal-value");

        var response = await WithAuditWritesRejectedAsync(() => SecretDetailsApi.RevealAsync(admin, details));

        await AssertAuditOutageAsync(response);
        Assert.DoesNotContain("it-failure-reveal-value", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_version_reveal_that_cannot_be_recorded_discloses_nothing()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-failure-unrecorded-version-reveal", value: "it-failure-archived-value");
        SecretDetailsApi.AssertNoContent(await SecretDetailsApi.RotateAsync(admin, details, "it-failure-live-value"));
        var version = await SecretDetailsApi.NewestVersionAsync(vault, details);

        var response = await WithAuditWritesRejectedAsync(() => SecretDetailsApi.RevealVersionAsync(admin, details, version));

        await AssertAuditOutageAsync(response);
        Assert.DoesNotContain("it-failure-archived-value", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_rotation_that_cannot_be_recorded_changes_nothing()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-failure-unrecorded-rotation", value: "it-failure-first-value");

        await AssertAuditOutageAsync(await WithAuditWritesRejectedAsync(() => SecretDetailsApi.RotateAsync(admin, details, "it-failure-lost-value")));

        Assert.Equal("it-failure-first-value", await UpstreamRotationTests.StoredValueAsync(vault, details));
        Assert.Equal(0, await SecretDetailsApi.VersionCountAsync(vault, details));
    }

    [Fact]
    public async Task A_restore_that_cannot_be_recorded_changes_nothing()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-failure-unrecorded-restore", value: "it-failure-first-value");
        SecretDetailsApi.AssertNoContent(await SecretDetailsApi.RotateAsync(admin, details, "it-failure-second-value"));
        var version = await SecretDetailsApi.NewestVersionAsync(vault, details);

        await AssertAuditOutageAsync(await WithAuditWritesRejectedAsync(() => SecretDetailsApi.RestoreAsync(admin, details, version)));

        Assert.Equal("it-failure-second-value", await UpstreamRotationTests.StoredValueAsync(vault, details));
        Assert.Equal(version, await SecretDetailsApi.NewestVersionAsync(vault, details));
    }

    [Fact]
    public async Task A_share_that_cannot_be_recorded_shares_nothing()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-failure-unrecorded-share");
        await vault.SignInNewAccountAsync("it-failure-unrecorded-share-recipient", IPAddress.Parse("10.167.0.3"), project: "IT-FAILURE-ELSEWHERE");

        await AssertAuditOutageAsync(await WithAuditWritesRejectedAsync(
            () => SecretDetailsApi.ShareAsync(admin, details, "it-failure-unrecorded-share-recipient")));

        Assert.False(await SecretSharing.HasAnyShareAsync(vault, details));
    }

    [Fact]
    public async Task A_share_revocation_that_cannot_be_recorded_keeps_the_share()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-failure-unrecorded-revoke");
        await vault.SignInNewAccountAsync("it-failure-unrecorded-revoke-recipient", IPAddress.Parse("10.167.0.4"), project: "IT-FAILURE-ELSEWHERE");
        SecretDetailsApi.AssertNoContent(await SecretDetailsApi.ShareAsync(admin, details, "it-failure-unrecorded-revoke-recipient"));
        var share = await SecretSharing.ShareOfAsync(vault, details);

        await AssertAuditOutageAsync(await WithAuditWritesRejectedAsync(() => SecretDetailsApi.RevokeAsync(admin, details, share.Id)));

        Assert.Equal(share.Id, (await SecretSharing.ShareOfAsync(vault, details)).Id);
    }

    [Fact]
    public async Task A_delete_that_cannot_be_recorded_keeps_the_secret()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var details = await SecretSharing.CreateSecretAsync(admin, "it-failure-unrecorded-delete");

        await AssertAuditOutageAsync(await WithAuditWritesRejectedAsync(() => SecretDetailsApi.DeleteAsync(admin, details)));

        Assert.True(await SecretDetailsApi.ExistsAsync(vault, details));
    }

    [Fact]
    public async Task An_upstream_rotation_that_cannot_be_recorded_puts_the_login_back_and_changes_nothing()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var password = ManagedTargetLogins.NewPassword();
        await ManagedTargetLogins.CreateAsync(vault, "it_failure_upstream", password);
        var details = await SecretSharing.CreateSecretAsync(admin, "it-failure-unrecorded-upstream", value: password);
        await SecretDetailsApi.MakeManagedAsync(vault, details, "it_failure_upstream");

        await AssertAuditOutageAsync(await WithAuditWritesRejectedAsync(() => SecretDetailsApi.RotateUpstreamAsync(admin, details)));

        Assert.True(await ManagedTargetLogins.OpensWithAsync(vault, "it_failure_upstream", password));
        Assert.Equal(password, await UpstreamRotationTests.StoredValueAsync(vault, details));
    }

    private async Task<HttpResponseMessage> WithAuditWritesRejectedAsync(Func<Task<HttpResponseMessage>> send)
    {
        await failure.RejectAuditWritesAsync();
        try
        {
            return await send();
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }

    private static Task AssertAuditOutageAsync(HttpResponseMessage response)
        => UiApiFailureContractTests.AssertProblemAsync(response, 503, "audit_unavailable");
}
