using System.Net;
using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(VaultCollection.Name)]
public sealed class RetiredAccountRoutesTests(VaultFixture vault)
{
    [Theory]
    [InlineData("POST", "/Admin/Users", HttpStatusCode.MethodNotAllowed)]
    [InlineData("DELETE", "/aDmIn/uSeRs/", HttpStatusCode.MethodNotAllowed)]
    [InlineData("GET", "/Admin/Users/{id}", HttpStatusCode.NotFound)]
    [InlineData("POST", "/Admin/CreateUser", HttpStatusCode.MethodNotAllowed)]
    [InlineData("PUT", "/aDmIn/cReAtEuSeR/", HttpStatusCode.MethodNotAllowed)]
    [InlineData("GET", "/Admin/CreateUser/{id}", HttpStatusCode.NotFound)]
    [InlineData("POST", "/Admin/EditUser/{id}", HttpStatusCode.MethodNotAllowed)]
    [InlineData("PATCH", "/aDmIn/eDiTuSeR/{id}/", HttpStatusCode.MethodNotAllowed)]
    [InlineData("GET", "/Admin/EditUser", HttpStatusCode.NotFound)]
    [InlineData("POST", "/Admin/ResetTotp/{id}", HttpStatusCode.NotFound)]
    [InlineData("GET", "/aDmIn/rEsEtToTp/{id}/", HttpStatusCode.NotFound)]
    [InlineData("PUT", "/ADMIN/RESETTOTP", HttpStatusCode.NotFound)]
    [InlineData("POST", "/Admin/DeleteUser/{id}", HttpStatusCode.NotFound)]
    [InlineData("HEAD", "/aDmIn/dElEtEuSeR/{id}/", HttpStatusCode.NotFound)]
    [InlineData("DELETE", "/ADMIN/DELETEUSER", HttpStatusCode.NotFound)]
    [InlineData("POST", "/Admin/SetEnabled/{id}", HttpStatusCode.NotFound)]
    [InlineData("GET", "/aDmIn/sEtEnAbLeD/{id}/", HttpStatusCode.NotFound)]
    [InlineData("PATCH", "/ADMIN/SETENABLED", HttpStatusCode.NotFound)]
    [InlineData("POST", "/Admin/ForceLogout/{id}", HttpStatusCode.NotFound)]
    [InlineData("GET", "/aDmIn/fOrCeLoGoUt/{id}/", HttpStatusCode.NotFound)]
    [InlineData("DELETE", "/ADMIN/FORCELOGOUT", HttpStatusCode.NotFound)]
    [InlineData("POST", "/Admin/Unlock/{id}", HttpStatusCode.NotFound)]
    [InlineData("HEAD", "/aDmIn/uNlOcK/{id}/", HttpStatusCode.NotFound)]
    [InlineData("PUT", "/ADMIN/UNLOCK", HttpStatusCode.NotFound)]
    public async Task A_retired_user_handler_cannot_change_an_account_or_its_sessions(string method, string path, HttpStatusCode expected)
    {
        var admin = await vault.AdminClientAsync();
        var username = "it-retired-user-" + Guid.NewGuid().ToString("N");
        (await UserAccounts.CreateAsync(admin, username)).EnsureSuccessStatusCode();
        var account = await ProfileHttp.AccountAsync(vault, username);
        using var scope = vault.Services.CreateScope();
        var revocations = scope.ServiceProvider.GetRequiredService<ISessionRevocationService>();
        var originalVersion = await revocations.CaptureAsync(account.Id, default);
        var before = await UserStateAsync(account.Id);
        var auditHead = await AuditTrail.HeadAsync(vault);

        using var response = await SendAsync(admin, method, path.Replace("{id}", account.Id.ToString()), account.Id);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await UserStateAsync(account.Id));
        Assert.Equal(originalVersion, await revocations.FindAsync(account.Id, default));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, auditHead));
    }

    [Theory]
    [InlineData("POST", "/ServiceAccounts", HttpStatusCode.MethodNotAllowed)]
    [InlineData("DELETE", "/sErViCeAcCoUnTs/", HttpStatusCode.MethodNotAllowed)]
    [InlineData("POST", "/ServiceAccounts/Index", HttpStatusCode.MethodNotAllowed)]
    [InlineData("PUT", "/sErViCeAcCoUnTs/iNdEx/", HttpStatusCode.MethodNotAllowed)]
    [InlineData("GET", "/ServiceAccounts/Index/{id}", HttpStatusCode.NotFound)]
    [InlineData("POST", "/ServiceAccounts/Create", HttpStatusCode.MethodNotAllowed)]
    [InlineData("PATCH", "/sErViCeAcCoUnTs/cReAtE/", HttpStatusCode.MethodNotAllowed)]
    [InlineData("GET", "/ServiceAccounts/Create/{id}", HttpStatusCode.NotFound)]
    [InlineData("POST", "/ServiceAccounts/Details/{id}", HttpStatusCode.MethodNotAllowed)]
    [InlineData("DELETE", "/sErViCeAcCoUnTs/dEtAiLs/{id}/", HttpStatusCode.MethodNotAllowed)]
    [InlineData("GET", "/ServiceAccounts/Details", HttpStatusCode.NotFound)]
    [InlineData("POST", "/ServiceAccounts/IssueKey/{id}", HttpStatusCode.NotFound)]
    [InlineData("GET", "/sErViCeAcCoUnTs/iSsUeKeY/{id}/", HttpStatusCode.NotFound)]
    [InlineData("PUT", "/SERVICEACCOUNTS/ISSUEKEY", HttpStatusCode.NotFound)]
    [InlineData("POST", "/ServiceAccounts/RevokeKey/{id}", HttpStatusCode.NotFound)]
    [InlineData("HEAD", "/sErViCeAcCoUnTs/rEvOkEkEy/{id}/", HttpStatusCode.NotFound)]
    [InlineData("DELETE", "/SERVICEACCOUNTS/REVOKEKEY", HttpStatusCode.NotFound)]
    [InlineData("POST", "/ServiceAccounts/SetEnabled/{id}", HttpStatusCode.NotFound)]
    [InlineData("GET", "/sErViCeAcCoUnTs/sEtEnAbLeD/{id}/", HttpStatusCode.NotFound)]
    [InlineData("PATCH", "/SERVICEACCOUNTS/SETENABLED", HttpStatusCode.NotFound)]
    [InlineData("POST", "/ServiceAccounts/Delete/{id}", HttpStatusCode.NotFound)]
    [InlineData("HEAD", "/sErViCeAcCoUnTs/dElEtE/{id}/", HttpStatusCode.NotFound)]
    [InlineData("DELETE", "/SERVICEACCOUNTS/DELETE", HttpStatusCode.NotFound)]
    public async Task A_retired_service_account_handler_cannot_change_an_account_or_its_keys(string method, string path, HttpStatusCode expected)
    {
        var admin = await vault.AdminClientAsync();
        var account = await ServiceAccountsApi.CreatedAsync(vault, admin, "it-retired-sa-" + Guid.NewGuid().ToString("N"));
        var key = await ServiceAccountsApi.IssuedAsync(admin, account.Id);
        var before = await ServiceStateAsync(account.Id);
        var auditHead = await AuditTrail.HeadAsync(vault);

        using var response = await SendAsync(admin, method, path.Replace("{id}", account.Id.ToString()), account.Id, key.KeyId);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await ServiceStateAsync(account.Id));
        Assert.Empty(await AuditTrail.RowsAfterAsync(vault, auditHead));
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient admin, string method, string path, Guid id, Guid? keyId = null)
        => await UiApiRequest.SendWithTokenAsync(admin, await UiApiRequest.AntiforgeryTokenAsync(admin), new HttpMethod(method),
            $"{path}?id={id}&keyId={keyId}", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["id"] = id.ToString(),
                ["keyId"] = keyId?.ToString() ?? "",
                ["enabled"] = "false",
                ["clearance"] = "3",
                ["projectKey"] = "RETIRED",
                ["ttlDays"] = "7",
                ["username"] = "retired-" + Guid.NewGuid().ToString("N"),
                ["firstName"] = "Retired",
                ["lastName"] = "Route",
                ["password"] = "Unused-retired-form-password",
                ["name"] = "retired-" + Guid.NewGuid().ToString("N"),
            }));

    private async Task<string> UserStateAsync(Guid id)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        return JsonSerializer.Serialize(await db.Users.AsNoTracking().Where(user => user.Id == id)
            .Select(user => new { user.Id, user.Clearance, user.ProjectKey, user.IsDisabled, user.CredentialGeneration, user.TotpEnabled, user.LastTotpStep, user.FailedAccessCount, user.LockedUntilUtc })
            .SingleOrDefaultAsync());
    }

    private async Task<string> ServiceStateAsync(Guid id)
    {
        using var scope = vault.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        return JsonSerializer.Serialize(await db.ServiceAccounts.AsNoTracking().Where(account => account.Id == id)
            .Select(account => new
            {
                account.Id,
                account.Name,
                account.Clearance,
                account.ProjectKey,
                account.IsDisabled,
                Keys = account.Keys.OrderBy(key => key.Id).Select(key => new { key.Id, key.ExpiresAtUtc, key.RevokedAtUtc, key.ClearanceCeiling, key.ProjectScope, key.MetadataOnly, key.AllowedCidrs }).ToArray()
            }).SingleOrDefaultAsync());
    }
}
