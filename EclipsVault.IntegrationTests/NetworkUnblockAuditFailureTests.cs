using System.Net;
using EclipsVault.Core.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(AuditFailureCollection.Name)]
public sealed class NetworkUnblockAuditFailureTests(AuditFailureFixture failure)
{
    [Fact]
    public async Task An_unblock_that_cannot_be_recorded_restores_the_original_block()
    {
        var vault = failure.Vault;
        var admin = await vault.AdminClientAsync();
        var blacklist = vault.Services.GetRequiredService<IIpBlacklist>();
        await blacklist.BlockAsync("198.51.100.113", "Honey-token 'Unblock regression' tripped");
        var original = Assert.Single(await blacklist.ListAsync());
        Assert.Equal(403, (await vault.SendFromAsync(IPAddress.Parse("198.51.100.113"), "/")).Response.StatusCode);
        var token = VaultFixture.FormTokenOf(await admin.GetStringAsync("/Admin/Networks"), "/Admin/Networks");
        var before = await AuditTrail.HeadAsync(vault);

        await failure.RejectAuditWritesAsync();
        try
        {
            using var response = await admin.PostAsync("/Admin/Unblock", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["network"] = "198.51.100.113/32",
                ["__RequestVerificationToken"] = token
            }));

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/Home/Error?code=503", response.Headers.Location?.OriginalString);
            Assert.Equal(original, Assert.Single(await blacklist.ListAsync()));
            Assert.Equal(403, (await vault.SendFromAsync(IPAddress.Parse("198.51.100.113"), "/")).Response.StatusCode);
            Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        }
        finally
        {
            await failure.RestoreAuditWritesAsync();
            await blacklist.UnblockAsync("198.51.100.113/32");
        }
    }
}
