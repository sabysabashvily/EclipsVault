using System.Net.Http.Json;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EclipsVault.IntegrationTests;

internal static class NetworksApi
{
    public const string Path = "/api/ui/networks";

    public static JsonContent AddContent(string range, string label = "") => JsonContent.Create(new { range, label });

    public static Task<HttpResponseMessage> AddAsync(HttpClient admin, string range, string label = "")
        => WriteAsync(admin, HttpMethod.Post, Path, AddContent(range, label));

    public static Task<HttpResponseMessage> TrustCurrentAddressAsync(HttpClient admin)
        => WriteAsync(admin, HttpMethod.Post, Path + "/trust-current-address");

    public static Task<HttpResponseMessage> RemoveAsync(HttpClient admin, Guid networkId)
        => WriteAsync(admin, HttpMethod.Delete, $"{Path}/{networkId}");

    public static Task<HttpResponseMessage> LiftAsync(HttpClient admin, string range)
        => WriteAsync(admin, HttpMethod.Post, Path + "/blocks/lift", JsonContent.Create(new { range }));

    public static async Task<HttpResponseMessage> WriteAsync(HttpClient admin, HttpMethod method, string path, HttpContent? content = null)
        => await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin), method, path, content);

    public static async Task<TrustedNetwork?> FindAsync(VaultFixture vault, string cidr)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        return await db.TrustedNetworks.AsNoTracking().SingleOrDefaultAsync(network => network.Cidr == cidr);
    }

    /// <summary>Removes a range a test trusted, so it never widens the network rule for later tests.</summary>
    public static async Task ForgetAsync(VaultFixture vault, string cidr)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        if (await db.TrustedNetworks.SingleOrDefaultAsync(network => network.Cidr == cidr) is { } network)
        {
            (await RemoveAsync(await vault.AdminClientAsync(), network.Id)).EnsureSuccessStatusCode();
        }
    }
}
