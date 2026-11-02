using System.Globalization;
using System.Net;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EclipsVault.IntegrationTests;

/// <summary>The Legacy Service accounts pages' forms, posted the way an administrator's browser posts them.</summary>
internal static class LegacyServiceAccounts
{
    public const string ServiceAccountsPage = "/ServiceAccounts";

    public const string CreatePage = "/ServiceAccounts/Create";

    public static async Task<HttpResponseMessage> CreateAsync(HttpClient admin, string name)
        => await admin.PostAsync(CreatePage, await CreateFormAsync(admin, name));

    /// <summary>A filled-in New service account form for a Secret account, ready to post.</summary>
    public static Task<FormUrlEncodedContent> CreateFormAsync(HttpClient admin, string name)
        => FormAsync(admin, new()
        {
            ["Name"] = name,
            ["Clearance"] = ((int)ClearanceLevel.Secret).ToString(CultureInfo.InvariantCulture),
            ["ProjectKey"] = "GLOBAL",
        });

    /// <summary>Issues a key with the Details page's defaults: no expiry, no ceiling and no other scope.</summary>
    public static Task<HttpResponseMessage> IssueKeyAsync(HttpClient admin, Guid serviceAccountId)
        => IssueKeyAsync(admin, serviceAccountId, new() { ["ttlDays"] = "0", ["clearanceCeiling"] = "0" });

    public static async Task<HttpResponseMessage> IssueKeyAsync(HttpClient admin, Guid serviceAccountId, Dictionary<string, string> options)
        => await admin.PostAsync($"/ServiceAccounts/IssueKey/{serviceAccountId}", await FormAsync(admin, options));

    public static async Task<HttpResponseMessage> RevokeKeyAsync(HttpClient admin, Guid serviceAccountId, Guid keyId)
        => await admin.PostAsync($"/ServiceAccounts/RevokeKey/{serviceAccountId}?keyId={keyId}", await FormAsync(admin, new()));

    public static async Task<HttpResponseMessage> SetEnabledAsync(HttpClient admin, Guid serviceAccountId, bool enabled)
        => await admin.PostAsync($"/ServiceAccounts/SetEnabled/{serviceAccountId}", await FormAsync(admin, new()
        {
            ["enabled"] = enabled ? "true" : "false",
        }));

    public static async Task<HttpResponseMessage> DeleteAsync(HttpClient admin, Guid serviceAccountId)
        => await admin.PostAsync($"/ServiceAccounts/Delete/{serviceAccountId}", await FormAsync(admin, new()));

    /// <summary>Creates a Secret service account through the form and reads it back.</summary>
    public static async Task<ServiceAccount> CreatedAsync(VaultFixture vault, HttpClient admin, string name)
    {
        var created = await CreateAsync(admin, name);
        if (created.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException($"Creating the service account {name} answered {(int)created.StatusCode}, not a redirect.");
        }

        return (await FindAsync(vault, name))!;
    }

    /// <summary>The stored service account with its keys, read in a request of its own.</summary>
    public static async Task<ServiceAccount?> FindAsync(VaultFixture vault, string name)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        return await db.ServiceAccounts.AsNoTracking().Include(account => account.Keys).SingleOrDefaultAsync(account => account.Name == name);
    }

    /// <summary>The page's flash message, as the administrator reads it after the redirect.</summary>
    public static async Task<string> PageAfterAsync(HttpClient admin, HttpResponseMessage redirect)
        => WebUtility.HtmlDecode(await admin.GetStringAsync(redirect.Headers.Location?.OriginalString
            ?? throw new InvalidOperationException($"The form answered {(int)redirect.StatusCode}, not a redirect.")));

    /// <summary>The form's fields with the antiforgery token the Service accounts page renders for this session.</summary>
    public static async Task<FormUrlEncodedContent> FormAsync(HttpClient admin, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = VaultFixture.FormTokenOf(await admin.GetStringAsync(ServiceAccountsPage), ServiceAccountsPage);
        return new FormUrlEncodedContent(fields);
    }
}
