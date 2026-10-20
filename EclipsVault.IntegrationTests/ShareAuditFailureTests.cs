using System.Net;
using EclipsVault.Core.Application.Abstractions;
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
        var details = await LegacySharing.CreateSecretAsync(await vault.AdminClientAsync(), "it-failure-scoped-secret");
        Assert.Equal(HttpStatusCode.Redirect, (await LegacySharing.ShareAsync(issuer.Client, details, "it-failure-scoped-recipient")).StatusCode);
        var share = await LegacySharing.ShareOfAsync(vault, details);
        Assert.Equal(HttpStatusCode.OK, (await recipient.Client.GetAsync(details)).StatusCode);
        var before = await LegacySharing.AuditHeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await LegacySharing.RevokeIssuedAsync(issuer.Client, share.Id);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/Home/Error?code=503", response.Headers.Location?.OriginalString);
            Assert.Equal(share.Id, (await LegacySharing.ShareOfAsync(vault, details)).Id);
            Assert.Contains("it-failure-scoped-secret", await recipient.Client.GetStringAsync("/Secrets/SharedWithMe"));
            Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await recipient.Client.GetAsync(details)).StatusCode);
    }

    [Fact]
    public async Task The_failing_audit_store_also_rejects_standalone_group_commits()
    {
        var vault = failure.Vault;
        var before = await LegacySharing.AuditHeadAsync(vault);

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

            Assert.Empty(await LegacySharing.AuditRowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }
}
