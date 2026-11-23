using System.Net;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(AuditFailureCollection.Name)]
public sealed class ShareAuditFailureTests(AuditFailureFixture failure)
{
    [Fact]
    public async Task A_failed_audit_insert_rolls_back_issuer_scoped_revocation_and_keeps_access()
    {
        var vault = failure.Vault;
        var issuer = await vault.SignInNewAccountAsync("it-failure-scoped-issuer", IPAddress.Parse("198.51.100.141"));
        var recipient = await vault.SignInNewAccountAsync("it-failure-scoped-recipient", IPAddress.Parse("198.51.100.142"), project: "AUDIT-FAILURE");
        var details = await SecretSharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-failure-scoped-secret");
        Assert.Equal(HttpStatusCode.NoContent, (await SecretDetailsApi.ShareAsync(issuer.Client, details, "it-failure-scoped-recipient")).StatusCode);
        var share = await SecretSharing.ShareOfAsync(vault, details);
        Assert.Equal(HttpStatusCode.OK, (await SecretDetailsApi.GetAsync(recipient.Client, details)).StatusCode);
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await UiApiRequest.WriteWithAntiforgeryAsync(issuer, HttpMethod.Delete, $"/api/ui/shared-by-me/{share.Id}");

            await UiApiFailureContractTests.AssertProblemAsync(response, 503, "audit_unavailable");
            Assert.Equal(share.Id, (await SecretSharing.ShareOfAsync(vault, details)).Id);
            Assert.Contains("it-failure-scoped-secret", await recipient.Client.GetStringAsync("/api/ui/shared-with-me"));
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await SecretDetailsApi.GetAsync(recipient.Client, details)).StatusCode);
    }

    /// <summary>
    /// The vault's own share service, from its own container, makes replacing the expired share the
    /// step that fails, with nothing else in the request writing first.
    /// </summary>
    [Fact]
    public async Task A_failed_audit_insert_rolls_back_replacing_an_expired_share_and_keeps_the_expired_one()
    {
        var vault = failure.Vault;
        var issuer = await vault.AdminClientAsync();
        var recipient = await vault.SignInNewAccountAsync("it-failure-expired-recipient", IPAddress.Parse("198.51.100.143"), project: "AUDIT-FAILURE");
        var details = await SecretSharing.CreateSecretAsync(issuer, "it-failure-expired-secret");
        Assert.Equal(HttpStatusCode.NoContent, (await SecretDetailsApi.ShareAsync(issuer, details, "it-failure-expired-recipient", ttlDays: 1)).StatusCode);
        var expired = await SecretSharing.ShareOfAsync(vault, details);
        await SecretSharing.ExpireShareAsync(vault, expired.Id);
        var before = await AuditTrail.HeadAsync(vault);
        using var scope = vault.Services.CreateScope();
        var shares = scope.ServiceProvider.GetRequiredService<ISecretGrantService>();

        await failure.RejectAuditWritesAsync();
        try
        {
            await Assert.ThrowsAsync<AuditWriteFailedException>(
                () => shares.GrantAsync(expired.SecretId, "it-failure-expired-recipient", ttlDays: null, CancellationToken.None));

            var kept = await SecretSharing.ShareOfAsync(vault, details);
            Assert.Equal(expired.Id, kept.Id);
            Assert.False(kept.IsActive(DateTimeOffset.UtcNow));
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await SecretDetailsApi.GetAsync(recipient.Client, details)).StatusCode);
    }

    [Fact]
    public async Task The_failing_audit_store_also_rejects_standalone_group_commits()
    {
        var vault = failure.Vault;
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            using var scope = vault.Services.CreateScope();
            var sink = scope.ServiceProvider.GetRequiredService<IAuditSink>();

            await Assert.ThrowsAsync<AuditWriteFailedException>(() => sink.WriteAsync(new AuditEntry
            {
                Action = AuditAction.SecretShared,
                ResourceType = "Secret",
                ResourceName = "it-failure-standalone",
                Details = "A standalone audit write must fail at the same store.",
            }, CancellationToken.None));

            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }
}
