using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Infrastructure.Distributed;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Persistence.Locking;
using EclipsVault.Web.Authentication;
using EclipsVault.Web.Extensions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.IntegrationTests;

[Collection(RedisVaultCollection.Name)]
public sealed class RedisSessionHttpTests(RedisVaultFixture fixture)
{
    [Fact]
    public async Task Store_failure_before_a_password_commit_refuses_the_change_and_keeps_every_session()
    {
        const string password = "It-store-uncommitted-password-2026!";
        const string replacementPassword = "It-store-refused-password-2026!";
        var owner = await fixture.Vault.SignInNewAccountAsync("it-redis-uncommitted", IPAddress.Parse("192.0.2.94"), password: password);
        var original = SessionApi.PrincipalOf(fixture.Vault, owner);
        var copied = fixture.Vault.CreateSession(owner.SessionCookie, IPAddress.Parse("192.0.2.95"));
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(owner.ProfileOwner!,
            new { kind = "ChangePassword", newPassword = replacementPassword });
        using var blocker = fixture.Vault.Services.CreateScope();
        var database = blocker.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync();
        var connectionId = await database.Database.SqlQuery<int>($"SELECT CAST(@@SPID AS int) AS Value").SingleAsync();
        // Holding the account lock stops the request before its locked recheck of the store.
        await blocker.ServiceProvider.GetRequiredService<IAuditChainLocker>().AcquireAsync(database,
            $"EclipsVault:Account:{original.GetUserId():D}", TimeSpan.FromSeconds(15), default);
        var change = ProfileApi.SendAsync(owner, HttpMethod.Post, "/api/ui/profile/password", new { operationId });
        try
        {
            await WaitForBlockedRequestAsync(database, connectionId);
            await fixture.Redis.PauseAsync();
            await transaction.RollbackAsync();

            var response = await change;

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("session_store_unavailable", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        finally
        {
            await transaction.DisposeAsync();
            await fixture.Redis.ResumeAsync();
            await change;
        }

        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync("/api/ui/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await copied.Client.GetAsync("/api/ui/me")).StatusCode);
        var account = await ProfileHttp.AccountAsync(fixture.Vault, "it-redis-uncommitted");
        Assert.Equal(0, account.CredentialGeneration);
        Assert.True(fixture.Vault.Services.GetRequiredService<IPasswordHasher>().Verify(password, account.PasswordHash, account.PasswordSalt));
    }

    [Fact]
    public async Task Store_failure_after_a_password_commit_reports_sign_in_required_and_keeps_the_change()
    {
        const string password = "It-store-commit-password-2026!";
        const string replacementPassword = "It-store-committed-password-2026!";
        var owner = await fixture.Vault.SignInNewAccountAsync("it-redis-committed", IPAddress.Parse("192.0.2.84"), password: password);
        var original = SessionApi.PrincipalOf(fixture.Vault, owner);
        var copied = fixture.Vault.CreateSession(owner.SessionCookie, IPAddress.Parse("192.0.2.85"));
        var operationId = await ProfileApi.VerifiedWithAuthenticatorAsync(owner.ProfileOwner!,
            new { kind = "ChangePassword", newPassword = replacementPassword });
        using var blocker = fixture.Vault.Services.CreateScope();
        var database = blocker.ServiceProvider.GetRequiredService<EclipsVaultDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync();
        var connectionId = await database.Database.SqlQuery<int>($"SELECT CAST(@@SPID AS int) AS Value").SingleAsync();
        // Holding the account's row, not its lock, lets the request pass its locked recheck of the
        // store and stop at the write, so the store fails only after the change can commit.
        await database.Database.ExecuteSqlAsync($"SELECT Id FROM Users WITH (UPDLOCK, ROWLOCK) WHERE Id = {original.GetUserId()}");
        var change = ProfileApi.SendAsync(owner, HttpMethod.Post, "/api/ui/profile/password", new { operationId });
        try
        {
            await WaitForBlockedRequestAsync(database, connectionId);
            await fixture.Redis.PauseAsync();
            await transaction.RollbackAsync();

            var response = await change;

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("changed_sign_in_required", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            Assert.Null(owner.SessionCookie);
            Assert.Equal(1, (await ProfileHttp.AccountAsync(fixture.Vault, "it-redis-committed")).CredentialGeneration);
        }
        finally
        {
            await transaction.DisposeAsync();
            await fixture.Redis.ResumeAsync();
            await change;
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await copied.Client.GetAsync("/api/ui/me")).StatusCode);
        var account = await ProfileHttp.AccountAsync(fixture.Vault, "it-redis-committed");
        Assert.True(fixture.Vault.Services.GetRequiredService<IPasswordHasher>().Verify(replacementPassword, account.PasswordHash, account.PasswordSalt));
    }

    [Fact]
    public async Task A_real_store_outage_blocks_protected_requests_but_allows_anonymous_local_cookie_clearing()
    {
        var owner = await fixture.Vault.SignInNewAccountAsync("it-redis-outage", IPAddress.Parse("192.0.2.80"));
        var accountId = SessionApi.PrincipalOf(fixture.Vault, owner).GetUserId();
        var before = await AuditTrail.HeadAsync(fixture.Vault);

        await fixture.Redis.PauseAsync();
        try
        {
            var protectedResponse = await owner.Client.GetAsync("/api/ui/me");
            Assert.Equal(HttpStatusCode.InternalServerError, protectedResponse.StatusCode);
            var tokenResponse = await owner.Client.GetAsync(LocalSignOut.Path);
            Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
            Assert.True(tokenResponse.Headers.CacheControl?.NoStore);
            var token = await tokenResponse.Content.ReadFromJsonAsync<LocalSignOut.TokenResponse>();
            using var clear = new HttpRequestMessage(HttpMethod.Post, LocalSignOut.Path);
            clear.Headers.Add("RequestVerificationToken", token!.RequestToken);

            var response = await owner.Client.SendAsync(clear);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("sign_out_incomplete", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            Assert.Null(owner.SessionCookie);
        }
        finally
        {
            await fixture.Redis.ResumeAsync();
        }

        Assert.DoesNotContain((await AuditTrail.RowsAfterAsync(fixture.Vault, before)), row => row.ResourceId == accountId);
    }

    [Fact]
    public async Task A_restored_populated_store_cannot_revive_sessions_or_pending_proofs_on_rotated_nodes()
    {
        const string password = "It-recovery-gate-password-2026!";
        var owner = await fixture.Vault.SignInNewAccountAsync("it-redis-recovery", IPAddress.Parse("192.0.2.81"), password: password);
        var pending = fixture.Vault.CreateSession(source: IPAddress.Parse("192.0.2.82"));
        await SignInProof.BeginPasswordAsync(pending.Client, "it-redis-recovery", password);
        var principal = SessionApi.PrincipalOf(fixture.Vault, owner);
        var accountId = principal.GetUserId();
        var options = fixture.Vault.Services.GetRequiredService<IOptions<RedisOptions>>().Value;
        var versionKey = $"{options.SessionKeyPrefix}:revocation-version:{accountId:N}";
        var devicesKey = $"{options.SessionKeyPrefix}:sessions:{accountId:N}";
        var savedVersion = (await fixture.Redis.Database.KeyDumpAsync(versionKey))!;
        var savedDevices = (await fixture.Redis.Database.KeyDumpAsync(devicesKey))!;
        await fixture.Vault.Services.GetRequiredService<ISessionRevocationService>().RevokeAsync(accountId);

        await fixture.Redis.Database.KeyDeleteAsync(versionKey);
        await fixture.Redis.Database.KeyDeleteAsync(devicesKey);
        await fixture.Redis.Database.KeyRestoreAsync(versionKey, savedVersion, TimeSpan.FromHours(24));
        await fixture.Redis.Database.KeyRestoreAsync(devicesKey, savedDevices, TimeSpan.FromHours(12));
        // Traffic resumes only on replacement nodes configured with the same new external incarnation.
        await using var nodeA = RecoveredNode();
        await using var nodeB = RecoveredNode();
        using var sessionA = ReplayOn(nodeA, owner.Cookies.GetCookieHeader(owner.Client.BaseAddress!));
        using var sessionB = ReplayOn(nodeB, owner.Cookies.GetCookieHeader(owner.Client.BaseAddress!));
        using var proofA = ReplayOn(nodeA, pending.Cookies.GetCookieHeader(pending.Client.BaseAddress!));
        using var proofB = ReplayOn(nodeB, pending.Cookies.GetCookieHeader(pending.Client.BaseAddress!));

        Assert.Equal(HttpStatusCode.Unauthorized, (await sessionA.GetAsync("/api/ui/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await sessionB.GetAsync("/api/ui/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await proofA.GetAsync("/Account/Totp")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await proofB.GetAsync("/Account/Totp")).StatusCode);
        var freshVersion = await nodeA.Services.GetRequiredService<ISessionRevocationService>().CaptureAsync(accountId);
        Assert.NotEqual(principal.GetSessionStampOrNull()!.RevocationVersion, freshVersion);
        Assert.Equal(freshVersion, await nodeB.Services.GetRequiredService<ISessionRevocationService>().FindAsync(accountId));
        Assert.Empty(await nodeA.Services.GetRequiredService<ISessionRegistry>().ListAsync(accountId, new(0, freshVersion)));
    }

    [Fact]
    public async Task Local_clearing_deletes_every_chunk_using_the_configured_cookie_path_and_domain()
    {
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .ConfigureServices(services => services.PostConfigure<CookieAuthenticationOptions>(
                CookieAuthenticationDefaults.AuthenticationScheme, options =>
                {
                    options.Cookie.Name = "Scoped.Session";
                    options.Cookie.Path = "/vault";
                    options.Cookie.Domain = "localhost";
                })));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        var token = await client.GetFromJsonAsync<LocalSignOut.TokenResponse>(LocalSignOut.Path);
        using var clear = new HttpRequestMessage(HttpMethod.Post, LocalSignOut.Path);
        clear.Headers.Add("RequestVerificationToken", token!.RequestToken);
        clear.Headers.Add("Cookie", "Scoped.Session=chunks-2; Scoped.SessionC1=first; Scoped.SessionC2=second");

        var response = await client.SendAsync(clear);

        var deleted = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(deleted, cookie => cookie.StartsWith("Scoped.Session=;", StringComparison.Ordinal));
        Assert.Contains(deleted, cookie => cookie.StartsWith("Scoped.SessionC1=;", StringComparison.Ordinal));
        Assert.Contains(deleted, cookie => cookie.StartsWith("Scoped.SessionC2=;", StringComparison.Ordinal));
        Assert.All(deleted, cookie => Assert.Contains("path=/vault", cookie, StringComparison.OrdinalIgnoreCase));
        Assert.All(deleted, cookie => Assert.Contains("domain=localhost", cookie, StringComparison.OrdinalIgnoreCase));
    }

    private static WebApplicationFactory<Program> RecoveredNode()
        => new WebApplicationFactory<Program>().WithWebHostBuilder(host => host.UseEnvironment("Production")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:SessionIncarnation"] = "after-recovery",
            })));

    private static async Task WaitForBlockedRequestAsync(EclipsVaultDbContext database, int connectionId)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await database.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sys.dm_exec_requests WHERE blocking_session_id = {connectionId}")
            .SingleAsync(deadline.Token) == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), deadline.Token);
        }
    }

    private static HttpClient ReplayOn(WebApplicationFactory<Program> host, string cookies)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add("Cookie", cookies);
        return client;
    }
}
