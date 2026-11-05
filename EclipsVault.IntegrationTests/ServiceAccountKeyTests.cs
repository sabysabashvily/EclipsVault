using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>An administrator issues and revokes a service account's API keys through the UI API.</summary>
[Collection(VaultCollection.Name)]
public sealed class ServiceAccountKeyTests(VaultFixture vault)
{
    [Fact]
    public async Task An_issued_key_authenticates_on_the_public_api_and_is_refused_once_revoked()
    {
        var admin = await vault.AdminClientAsync();
        var apiKey = await vault.IssueApiKeyAsync(admin, "it-key-revoked-at-once");
        var account = await ServiceAccountsApi.FindAsync(vault, "it-key-revoked-at-once");
        var service = vault.CreateClient();
        service.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        Assert.Equal(HttpStatusCode.OK, (await service.GetAsync("/api/v1/secrets")).StatusCode);

        var revoked = await ServiceAccountsApi.RevokeKeyAsync(admin, account!.Id, Assert.Single(account.Keys).Id);

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await service.GetAsync("/api/v1/secrets")).StatusCode);
    }

    [Fact]
    public async Task A_key_revoked_under_another_service_account_is_not_found_and_keeps_working()
    {
        var admin = await vault.AdminClientAsync();
        var apiKey = await vault.IssueApiKeyAsync(admin, "it-key-owner");
        var owner = await ServiceAccountsApi.FindAsync(vault, "it-key-owner");
        var other = await ServiceAccountsApi.CreatedAsync(vault, admin, "it-key-other-account");
        var before = await AuditTrail.HeadAsync(vault);

        var response = await ServiceAccountsApi.RevokeKeyAsync(admin, other.Id, Assert.Single(owner!.Keys).Id);

        await UiApiFailureContractTests.AssertProblemAsync(response, 404, "key_not_found");
        Assert.Null(Assert.Single((await ServiceAccountsApi.FindAsync(vault, "it-key-owner"))!.Keys).RevokedAtUtc);
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
        var service = vault.CreateClient();
        service.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        Assert.Equal(HttpStatusCode.OK, (await service.GetAsync("/api/v1/secrets")).StatusCode);
    }

    [Fact]
    public async Task Revoking_a_revoked_key_records_nothing()
    {
        var admin = await vault.AdminClientAsync();
        var account = await ServiceAccountsApi.CreatedAsync(vault, admin, "it-key-revoked-twice");
        await ServiceAccountsApi.IssueKeyAsync(admin, account.Id);
        var key = Assert.Single((await ServiceAccountsApi.FindAsync(vault, "it-key-revoked-twice"))!.Keys);
        Assert.Equal(HttpStatusCode.NoContent, (await ServiceAccountsApi.RevokeKeyAsync(admin, account.Id, key.Id)).StatusCode);
        var before = await AuditTrail.HeadAsync(vault);

        Assert.Equal(HttpStatusCode.NoContent, (await ServiceAccountsApi.RevokeKeyAsync(admin, account.Id, key.Id)).StatusCode);

        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, before));
    }

    [Fact]
    public async Task The_audit_row_for_an_issued_key_carries_its_prefix_and_never_the_key()
    {
        var admin = await vault.AdminClientAsync();
        var apiKey = await vault.IssueApiKeyAsync(admin, "it-key-audited-by-prefix");
        var account = await ServiceAccountsApi.FindAsync(vault, "it-key-audited-by-prefix");
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();

        var issued = await db.AuditLogs.AsNoTracking()
            .SingleAsync(row => row.Action == AuditAction.ApiKeyIssued && row.ResourceId == account!.Id);

        Assert.Contains(Assert.Single(account!.Keys).Prefix, issued.Details, StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, issued.Details, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("it-key-unknown-ceiling", "{\"expiresInDays\":0,\"clearanceCeiling\":7}", "clearanceCeiling", "A clearance ceiling must be one of the vault's clearance levels below the service account's, or none.", new[] { "clearanceCeiling" })]
    [InlineData("it-key-ceiling-not-below", "{\"expiresInDays\":0,\"clearanceCeiling\":3}", "clearanceCeiling", "A clearance ceiling must be one of the vault's clearance levels below the service account's, or none.", new[] { "clearanceCeiling" })]
    [InlineData("it-key-negative-expiry", "{\"expiresInDays\":-1}", "expiresInDays", "A key expires after 0 to 3650 days, where 0 means it never expires.", new[] { "expiresInDays" })]
    [InlineData("it-key-unbounded-expiry", "{\"expiresInDays\":3651}", "expiresInDays", "A key expires after 0 to 3650 days, where 0 means it never expires.", new[] { "expiresInDays" })]
    [InlineData("it-key-overflowing-expiry", "{\"expiresInDays\":99999999999}", "expiresInDays", "This field is missing or invalid.", new[] { "expiresInDays", "request" })]
    [InlineData("it-key-named-ceiling", "{\"expiresInDays\":0,\"clearanceCeiling\":\"Elevated\"}", "clearanceCeiling", "This field is missing or invalid.", new[] { "clearanceCeiling", "request" })]
    public async Task An_invalid_key_option_returns_its_field_message_and_issues_no_key(string name, string json, string field, string message, string[] errorFields)
    {
        var admin = await vault.AdminClientAsync();
        var account = await ServiceAccountsApi.CreatedAsync(vault, admin, name);
        var response = await ServiceAccountsApi.IssueKeyAsync(admin, account.Id, new StringContent(json, Encoding.UTF8, "application/json"));

        await ServiceAccountsApi.AssertValidationAsync(response);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.Equal(errorFields, ProfileApi.NamesOf(errors));
        Assert.Equal(message, errors.GetProperty(field)[0].GetString());
        Assert.Empty((await ServiceAccountsApi.FindAsync(vault, name))!.Keys);
    }
}
