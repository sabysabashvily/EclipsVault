using System.Net;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Sharing a secret at or above the step-up sensitivity, from its page or by approving a request for it,
/// needs a sign-in or step-up within the last ten minutes, as revealing it does. Each reviewer is a GLOBAL
/// member cleared for Secret, signed in from a trusted address of its own (10.0.0.0/8); a stale copy of
/// its session claims a strong sign-in eleven minutes ago.
/// </summary>
[Collection(VaultCollection.Name)]
public sealed class SharingStepUpTests(VaultFixture vault)
{
    [Fact]
    public async Task A_stale_session_approving_a_request_for_a_Secret_secret_is_asked_to_step_up_and_shares_nothing()
    {
        var (reviewer, request) = await PendingRequestAsync("it-stepup-approve-stale", "Secret", "10.169.0.1", "10.169.0.2");
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, reviewer, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("10.169.0.1"));

        var response = await AccessRequestsApi.ApproveAsync(stale.Client, request.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(("step_up_required", "code"),
            (problem.RootElement.GetProperty("code").GetString(), problem.RootElement.GetProperty("proof").GetString()));
        Assert.Equal(AccessRequestStatus.Pending, await AccessRequestsApi.StatusOfAsync(vault, request.Id));
    }

    [Fact]
    public async Task A_fresh_session_approves_a_request_for_a_Secret_secret()
    {
        var (reviewer, request) = await PendingRequestAsync("it-stepup-approve-fresh", "Secret", "10.169.0.3", "10.169.0.4");

        var response = await AccessRequestsApi.ApproveAsync(reviewer.Client, request.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(AccessRequestStatus.Approved, await AccessRequestsApi.StatusOfAsync(vault, request.Id));
    }

    [Fact]
    public async Task A_stale_session_approving_a_request_for_an_Internal_secret_is_never_asked()
    {
        var (reviewer, request) = await PendingRequestAsync("it-stepup-approve-internal", "Internal", "10.169.0.5", "10.169.0.6");
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, reviewer, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("10.169.0.5"));

        var response = await AccessRequestsApi.ApproveAsync(stale.Client, request.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(AccessRequestStatus.Approved, await AccessRequestsApi.StatusOfAsync(vault, request.Id));
    }

    [Fact]
    public async Task A_stale_session_sharing_a_Secret_secret_is_asked_to_step_up_and_shares_nothing()
    {
        var (issuer, details) = await SecretToShareAsync("it-stepup-share-stale", "Secret", "10.169.0.7", "10.169.0.8");
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, issuer, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("10.169.0.7"));
        var before = await AuditTrail.HeadAsync(vault);

        var response = await SecretDetailsApi.ShareAsync(stale.Client, details, "it-stepup-share-stale-recipient");

        var problem = await SecretsApi.AssertProblemAsync(response, 403, "step_up_required");
        Assert.Equal(("Confirm it's you before sharing this secret.", "code"),
            (problem.GetProperty("title").GetString(), problem.GetProperty("proof").GetString()));
        Assert.False(await SecretSharing.HasAnyShareAsync(vault, details));
        Assert.Empty(await SecretDetailsApi.ActionsAfterAsync(vault, before, details));
    }

    [Fact]
    public async Task A_fresh_session_shares_a_Secret_secret()
    {
        var (issuer, details) = await SecretToShareAsync("it-stepup-share-fresh", "Secret", "10.169.0.9", "10.169.0.10");

        var response = await SecretDetailsApi.ShareAsync(issuer.Client, details, "it-stepup-share-fresh-recipient");

        SecretDetailsApi.AssertNoContent(response);
        Assert.True(await SecretSharing.HasAnyShareAsync(vault, details));
    }

    [Fact]
    public async Task A_stale_session_sharing_an_Internal_secret_is_never_asked()
    {
        var (issuer, details) = await SecretToShareAsync("it-stepup-share-internal", "Internal", "10.169.0.11", "10.169.0.12");
        var stale = vault.CreateSession(SessionApi.WithStrongAuthenticationAt(vault, issuer, DateTimeOffset.UtcNow.AddMinutes(-11)), IPAddress.Parse("10.169.0.11"));

        var response = await SecretDetailsApi.ShareAsync(stale.Client, details, "it-stepup-share-internal-recipient");

        SecretDetailsApi.AssertNoContent(response);
        Assert.True(await SecretSharing.HasAnyShareAsync(vault, details));
    }

    /// <summary>A reviewer and a pending request for a GLOBAL secret, filed by a Secret-cleared account of another project.</summary>
    private async Task<(VaultSession Reviewer, Core.Domain.Entities.AccessRequest Request)> PendingRequestAsync(
        string name, string sensitivity, string reviewerAddress, string requesterAddress)
    {
        var (reviewer, details) = await SecretToShareAsync(name, sensitivity, reviewerAddress, requesterAddress);
        return (reviewer, await LegacyAccessRequests.PendingAsync(vault, details, $"{name}-recipient"));
    }

    /// <summary>A GLOBAL secret, a reviewer who may share it, and a Secret-cleared account of another project to share it with.</summary>
    private async Task<(VaultSession Issuer, string Details)> SecretToShareAsync(
        string name, string sensitivity, string issuerAddress, string recipientAddress)
    {
        var details = await SecretSharing.CreateSecretAsync(await vault.AdminClientAsync(), $"{name}-secret", sensitivity: sensitivity);
        var issuer = await vault.SignInNewAccountAsync($"{name}-reviewer", IPAddress.Parse(issuerAddress), ClearanceLevel.Secret);
        await vault.SignInNewAccountAsync($"{name}-recipient", IPAddress.Parse(recipientAddress), ClearanceLevel.Secret, "IT-STEPUP-ELSEWHERE");
        return (issuer, details);
    }
}
