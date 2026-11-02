using System.Net;
using System.Net.Http.Headers;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>An administrator issues and revokes a service account's API keys through the Legacy pages.</summary>
[Collection(VaultCollection.Name)]
public sealed class ServiceAccountKeyTests(VaultFixture vault)
{
    [Fact]
    public async Task An_issued_key_authenticates_on_the_public_api_and_is_refused_once_revoked()
    {
        var admin = await vault.AdminClientAsync();
        var apiKey = await vault.IssueApiKeyAsync(admin, "it-key-revoked-at-once");
        var account = await LegacyServiceAccounts.FindAsync(vault, "it-key-revoked-at-once");
        var service = vault.CreateClient();
        service.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        Assert.Equal(HttpStatusCode.OK, (await service.GetAsync("/api/v1/secrets")).StatusCode);

        var revoked = await LegacyServiceAccounts.RevokeKeyAsync(admin, account!.Id, Assert.Single(account.Keys).Id);

        Assert.Contains("API key revoked.", await LegacyServiceAccounts.PageAfterAsync(admin, revoked), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await service.GetAsync("/api/v1/secrets")).StatusCode);
    }

    [Fact]
    public async Task A_key_revoked_under_another_service_account_is_not_found_and_keeps_working()
    {
        var admin = await vault.AdminClientAsync();
        var apiKey = await vault.IssueApiKeyAsync(admin, "it-key-owner");
        var owner = await LegacyServiceAccounts.FindAsync(vault, "it-key-owner");
        var other = await LegacyServiceAccounts.CreatedAsync(vault, admin, "it-key-other-account");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await LegacyServiceAccounts.RevokeKeyAsync(admin, other.Id, Assert.Single(owner!.Keys).Id);

        Assert.Contains("Key not found.", await LegacyServiceAccounts.PageAfterAsync(admin, response), StringComparison.Ordinal);
        Assert.Null(Assert.Single((await LegacyServiceAccounts.FindAsync(vault, "it-key-owner"))!.Keys).RevokedAtUtc);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        var service = vault.CreateClient();
        service.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        Assert.Equal(HttpStatusCode.OK, (await service.GetAsync("/api/v1/secrets")).StatusCode);
    }

    [Fact]
    public async Task Revoking_a_revoked_key_records_nothing()
    {
        var admin = await vault.AdminClientAsync();
        var account = await LegacyServiceAccounts.CreatedAsync(vault, admin, "it-key-revoked-twice");
        await LegacyServiceAccounts.IssueKeyAsync(admin, account.Id);
        var key = Assert.Single((await LegacyServiceAccounts.FindAsync(vault, "it-key-revoked-twice"))!.Keys);
        await LegacyServiceAccounts.RevokeKeyAsync(admin, account.Id, key.Id);
        var before = await AuditTrail.HeadAsync(vault);

        await LegacyServiceAccounts.RevokeKeyAsync(admin, account.Id, key.Id);

        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task The_audit_row_for_an_issued_key_carries_its_prefix_and_never_the_key()
    {
        var admin = await vault.AdminClientAsync();
        var apiKey = await vault.IssueApiKeyAsync(admin, "it-key-audited-by-prefix");
        var account = await LegacyServiceAccounts.FindAsync(vault, "it-key-audited-by-prefix");
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();

        var issued = await db.AuditLogs.AsNoTracking()
            .SingleAsync(row => row.Action == AuditAction.ApiKeyIssued && row.ResourceId == account!.Id);

        Assert.Contains(Assert.Single(account!.Keys).Prefix, issued.Details, StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, issued.Details, StringComparison.Ordinal);
    }

    /// <summary>The service account is Secret, so Elevated is the highest ceiling it accepts.</summary>
    [Theory]
    [InlineData("it-key-unknown-ceiling", "clearanceCeiling", "7", "A clearance ceiling must be one of the vault's clearance levels below the service account's, or none.")]
    [InlineData("it-key-ceiling-not-below", "clearanceCeiling", "3", "A clearance ceiling must be one of the vault's clearance levels below the service account's, or none.")]
    [InlineData("it-key-negative-expiry", "ttlDays", "-1", "A key expires after 0 to 3650 days, where 0 means it never expires.")]
    [InlineData("it-key-unbounded-expiry", "ttlDays", "3651", "A key expires after 0 to 3650 days, where 0 means it never expires.")]
    [InlineData("it-key-overflowing-expiry", "ttlDays", "99999999999", "Enter the expiry as a whole number of days and choose the clearance ceiling from the list.")]
    [InlineData("it-key-named-ceiling", "clearanceCeiling", "Elevated", "Enter the expiry as a whole number of days and choose the clearance ceiling from the list.")]
    public async Task A_key_option_outside_its_rule_is_refused_with_its_message_and_issues_no_key(
        string serviceAccount, string option, string value, string message)
    {
        var admin = await vault.AdminClientAsync();
        var account = await LegacyServiceAccounts.CreatedAsync(vault, admin, serviceAccount);
        var options = new Dictionary<string, string> { ["ttlDays"] = "0", ["clearanceCeiling"] = "0" };
        options[option] = value;

        var response = await LegacyServiceAccounts.IssueKeyAsync(admin, account.Id, options);

        Assert.Contains(message, await LegacyServiceAccounts.PageAfterAsync(admin, response), StringComparison.Ordinal);
        Assert.Empty((await LegacyServiceAccounts.FindAsync(vault, serviceAccount))!.Keys);
    }
}
