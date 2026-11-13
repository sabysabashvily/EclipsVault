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
/// Opening, verifying, checkpointing and exporting the audit trail happen only with their audit row. Each test
/// signs in with auditing working, then rejects audit writes, so the audited action is the step that fails.
/// </summary>
[Collection(AuditFailureCollection.Name)]
public sealed class AuditLogAuditFailureTests(AuditFailureFixture failure)
{
    [Theory]
    [InlineData("GET", "/api/ui/audit", "198.18.143.71")]
    [InlineData("POST", "/api/ui/audit/verify", "198.18.143.72")]
    [InlineData("POST", "/api/ui/audit/checkpoints", "198.18.143.73")]
    [InlineData("POST", "/api/ui/audit/export", "198.18.143.74")]
    public async Task An_access_that_cannot_be_recorded_answers_audit_unavailable_and_changes_nothing(string method, string path, string source)
    {
        var vault = failure.Vault;
        // Signed in just now, so Export's fresh-proof rule passes and recording is what fails.
        var admin = (await vault.SignInNewAccountAsync("it-audit-down-" + source.Split('.').Last(), IPAddress.Parse(source),
            ClearanceLevel.TopSecret)).Client;
        var token = await UiApiRequest.AntiforgeryTokenAsync(admin);
        var checkpoints = await CheckpointCountAsync(vault);
        var before = await AuditTrail.HeadAsync(vault);

        HttpResponseMessage response;
        await failure.RejectAuditWritesAsync();
        try
        {
            response = await UiApiRequest.SendWithTokenAsync(admin, token, new HttpMethod(method), path);
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
        }

        await UiApiFailureContractTests.AssertProblemAsync(response, 503, "audit_unavailable");
        Assert.Equal("The audit trail is unavailable, so the vault refused the request.",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.Equal(checkpoints, await CheckpointCountAsync(vault));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    private static async Task<int> CheckpointCountAsync(VaultFixture vault)
    {
        using var scope = vault.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().AuditCheckpoints.CountAsync();
    }
}
