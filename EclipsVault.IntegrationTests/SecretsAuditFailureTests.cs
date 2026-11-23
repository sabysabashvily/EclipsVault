using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>A new secret is stored only with its audit row.</summary>
[Collection(AuditFailureCollection.Name)]
public sealed class SecretsAuditFailureTests(AuditFailureFixture failure)
{
    [Fact]
    public async Task A_create_that_cannot_be_recorded_stores_no_secret()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var token = await UiApiRequest.AntiforgeryTokenAsync(admin);
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            var response = await UiApiRequest.SendWithTokenAsync(admin, token, HttpMethod.Post, SecretsApi.Path,
                SecretsApi.CreateContent("it-unrecorded-secret"));

            await UiApiFailureContractTests.AssertProblemAsync(response, 503, "audit_unavailable");
            using var scope = vault.Services.CreateScope();
            Assert.False(await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Secrets
                .AnyAsync(secret => secret.Name == "it-unrecorded-secret"));
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }
    }
}
