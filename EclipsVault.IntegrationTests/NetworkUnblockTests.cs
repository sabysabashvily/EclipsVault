using System.Net;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class NetworkUnblockTests(VaultFixture vault)
{
    [Fact]
    public async Task The_legacy_form_lifts_a_block_with_one_event_and_its_existing_success_message()
    {
        var admin = await vault.AdminClientAsync();
        var blacklist = vault.Services.GetRequiredService<IIpBlacklist>();
        await blacklist.BlockAsync("198.51.100.114", "Honey-token 'Legacy lift' tripped");
        var token = await FormTokenAsync(admin);
        var before = await AuditTrail.HeadAsync(vault);

        using var response = await PostAsync(admin, "198.51.100.114/32", token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Networks", response.Headers.Location?.OriginalString);
        Assert.False(await blacklist.IsBlockedAsync(IPAddress.Parse("198.51.100.114")));
        var row = Assert.Single(await AuditTrail.RowsAfterAsync(vault, before));
        Assert.Equal(AuditAction.IpRangeUnblocked, row.Action);
        Assert.Equal("198.51.100.114/32", row.ResourceName);
        Assert.Equal("TrustedNetwork", row.ResourceType);
        Assert.Equal("Intrusion-defence block lifted", row.Details);
        Assert.Equal(VaultFixture.AdminUsername, row.Username);
        Assert.Contains("Block on 198.51.100.114/32 lifted.", await admin.GetStringAsync("/Admin/Networks"));
    }

    [Fact]
    public async Task The_legacy_form_reports_an_absent_block_without_recording_a_lift()
    {
        var admin = await vault.AdminClientAsync();
        var token = await FormTokenAsync(admin);
        var before = await AuditTrail.HeadAsync(vault);

        using var response = await PostAsync(admin, "198.51.100.115/32", token);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Networks", response.Headers.Location?.OriginalString);
        Assert.Contains("That range is not currently blocked.", await admin.GetStringAsync("/Admin/Networks"));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task The_legacy_form_cannot_lift_a_block_without_antiforgery()
    {
        var admin = await vault.AdminClientAsync();
        var blacklist = vault.Services.GetRequiredService<IIpBlacklist>();
        await blacklist.BlockAsync("198.51.100.116", "Honey-token 'Antiforgery' tripped");
        var before = await AuditTrail.HeadAsync(vault);

        using var response = await PostAsync(admin, "198.51.100.116/32", "");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await blacklist.IsBlockedAsync(IPAddress.Parse("198.51.100.116")));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        await blacklist.UnblockAsync("198.51.100.116/32");
    }

    [Fact]
    public async Task The_legacy_restore_failure_page_does_not_claim_the_lift_was_refused()
    {
        using var response = await vault.CreateClient().GetAsync("/Home/Error?code=503&failure=unblock_unrecorded");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var page = await response.Content.ReadAsStringAsync();
        Assert.Contains("Block lift could not be recorded", page);
        Assert.Contains("The block was lifted, but recording and restoring it failed. Check the range before retrying.", page);
        Assert.DoesNotContain("the operation was refused", page);
    }

    private static async Task<string> FormTokenAsync(HttpClient admin)
        => VaultFixture.FormTokenOf(await admin.GetStringAsync("/Admin/Networks"), "/Admin/Networks");

    private static Task<HttpResponseMessage> PostAsync(HttpClient admin, string network, string token)
        => admin.PostAsync("/Admin/Unblock", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["network"] = network,
            ["__RequestVerificationToken"] = token
        }));
}
