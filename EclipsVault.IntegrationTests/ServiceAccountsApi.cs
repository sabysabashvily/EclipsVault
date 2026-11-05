using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Web.Models.Ui;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

internal static class ServiceAccountsApi
{
    public const string Path = "/api/ui/service-accounts";

    public static JsonContent CreateContent(string name) => JsonContent.Create(new { name, clearance = 3, project = "GLOBAL" });

    public static Task<HttpResponseMessage> CreateAsync(HttpClient admin, string name) => WriteAsync(admin, HttpMethod.Post, Path, CreateContent(name));

    public static Task<HttpResponseMessage> IssueKeyAsync(HttpClient admin, Guid id)
        => IssueKeyAsync(admin, id, JsonContent.Create(new { expiresInDays = 0 }));

    public static Task<HttpResponseMessage> IssueKeyAsync(HttpClient admin, Guid id, HttpContent content)
        => WriteAsync(admin, HttpMethod.Post, $"{Path}/{id}/keys", content);

    public static Task<HttpResponseMessage> RevokeKeyAsync(HttpClient admin, Guid id, Guid keyId)
        => WriteAsync(admin, HttpMethod.Delete, $"{Path}/{id}/keys/{keyId}");

    public static Task<HttpResponseMessage> SetEnabledAsync(HttpClient admin, Guid id, bool enabled)
        => WriteAsync(admin, HttpMethod.Post, $"{Path}/{id}/{(enabled ? "enable" : "disable")}");

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient admin, Guid id)
        => WriteAsync(admin, HttpMethod.Delete, $"{Path}/{id}");

    public static async Task<HttpResponseMessage> WriteAsync(HttpClient admin, HttpMethod method, string path, HttpContent? content = null)
        => await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin), method, path, content);

    public static async Task<ServiceAccount> CreatedAsync(VaultFixture vault, HttpClient admin, string name)
    {
        (await CreateAsync(admin, name)).EnsureSuccessStatusCode();
        return (await FindAsync(vault, name))!;
    }

    public static async Task<ServiceAccount?> FindAsync(VaultFixture vault, string name)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        return await db.ServiceAccounts.AsNoTracking().Include(account => account.Keys).SingleOrDefaultAsync(account => account.Name == name);
    }

    public static async Task AssertValidationAsync(HttpResponseMessage response)
    {
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["code", "errors", "status", "title"], ProfileApi.NamesOf(body.RootElement));
        Assert.Equal("validation_failed", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(400, body.RootElement.GetProperty("status").GetInt32());
        Assert.NotEmpty(body.RootElement.GetProperty("errors").EnumerateObject());
    }

    public static async Task<IssuedServiceAccountKeyResponse> IssuedAsync(HttpClient admin, Guid id)
    {
        var response = await IssueKeyAsync(admin, id);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IssuedServiceAccountKeyResponse>()
            ?? throw new InvalidOperationException("Key issuance returned no response.");
    }
}
