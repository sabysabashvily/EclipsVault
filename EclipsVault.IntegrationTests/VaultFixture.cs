using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Testcontainers.MsSql;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// One real vault per test run: the unmodified <c>Program</c> entry point, in Production, against a
/// SQL Server container (ADR-0010). Authentication and persistence are real. It is configured the way a container
/// deployment configures it, through environment variables, so startup runs its real tasks: the
/// crypto check, migrations, the bootstrap administrator, the legacy-blob upgrade and the audit chain.
/// Only the auth throttle's clock is frozen, so HTTP budget assertions cannot cross a rate window.
/// The other addition is the test-only endpoints in <c>TestOnlyEndpoints.cs</c>, which raise each failure
/// the error mapping must handle; they live in this assembly, so the vault itself never has them.
/// </summary>
public sealed partial class VaultFixture : IAsyncLifetime
{
    /// <summary>
    /// SQL Server 2025, pinned by digest so a run is repeatable. Local development (<c>docker-compose.yml</c>)
    /// runs 2022; the vault supports both.
    /// </summary>
    private const string SqlServerImage =
        "mcr.microsoft.com/mssql/server:2025-latest@sha256:ca62ad2d75975074cbc9ac39a1329cd88af9cd3ea2ffb59d6302579a97cf32f4";

    public const string AdminUsername = "vault-admin";

    public const string SessionCookie = "EclipsVault.Session";

    private static readonly Uri BaseAddress = new("https://localhost");

    // The vault signs in to its database as sa, so the password is per run rather than Testcontainers'
    // published default. SQL Server requires upper and lower case, a digit and a symbol.
    private readonly MsSqlContainer _sql = new MsSqlBuilder(SqlServerImage)
        .WithName($"{Environment.GetEnvironmentVariable("ECLIPSVAULT_TEST_RESOURCE_PREFIX") ?? "eclipsvault-it"}-sql-{Guid.NewGuid():N}")
        .WithPassword("Sa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
        .Build();

    private readonly DirectoryInfo _keyRing = Directory.CreateTempSubdirectory("eclipsvault-it-keys-");
    private readonly List<string> _environment = [];

    /// <summary>The bootstrap administrator's password, chosen per run as an operator would.</summary>
    private readonly string _adminPassword = "It-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private WebApplicationFactory<Program>? _factory;
    private Task<HttpClient>? _adminClient;

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();

        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SetEnvironment("ConnectionStrings__DefaultConnection",
            new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "EclipsVault" }.ConnectionString);
        SetEnvironment("ECLIPSVAULT_KEK", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        SetEnvironment("ECLIPSVAULT_AUDIT_SIGNING_KEY", Convert.ToBase64String(signingKey.ExportPkcs8PrivateKey()));
        SetEnvironment("DataProtection__KeyRingPath", _keyRing.FullName);
        SetEnvironment("Seed__AdminPassword", _adminPassword);
        // The single-node install with no deploy pipeline, which lets the vault apply its own migrations.
        SetEnvironment("Database__MigrateOnStartup", "true");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host => host
                .UseEnvironment("Production")
                .ConfigureTestServices(services =>
                {
                    services.AddMvcCore().AddApplicationPart(typeof(VaultFixture).Assembly);
                    // Keep the real counter, cache and configured thresholds. Freeze only its window;
                    // TOTP, cookie expiry and all other services retain the production clock.
                    services.AddSingleton<IAuthThrottle>(sp => new InMemoryAuthThrottle(
                        sp.GetRequiredService<IMemoryCache>(),
                        sp.GetRequiredService<IOptions<AuthThrottleOptions>>(),
                        new AuthThrottleClock()));
                }));

        // Program catches a refused boot, logs it and exits 1, so the factory still returns a host:
        // one that never started. Fail here, pointing at the log, rather than in every test.
        var services = _factory.Services;
        if (!services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "The vault refused to start. Its log says why: rerun with `--verbosity normal` and look for 'terminated unexpectedly'.");
        }

        // The factory defaults to Development, which would seed the demo accounts this suite must not use.
        var environment = services.GetRequiredService<IHostEnvironment>().EnvironmentName;
        if (environment != Environments.Production)
        {
            throw new InvalidOperationException($"The vault under test runs as {environment}, not Production.");
        }
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _sql.DisposeAsync();
        foreach (var name in _environment)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        _keyRing.Delete(recursive: true);
    }

    /// <summary>A client with no session: its own cookies, HTTPS, and redirects left for the test to see.</summary>
    public HttpClient CreateClient() => _factory!.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = BaseAddress,
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    /// <summary>
    /// The bootstrap administrator's session, signed in once per run through the vault's real sign-in:
    /// the password, then enrolling an authenticator and confirming it with a real code, as the first
    /// sign-in of a new vault does. There is no test-only authentication scheme.
    /// </summary>
    public Task<HttpClient> AdminClientAsync() => _adminClient ??= SignInAdministratorAsync();

    /// <summary>
    /// Issues a real API key the way an administrator does: create a service account, issue it a key,
    /// and copy the key from the one page that shows it.
    /// </summary>
    public async Task<string> IssueApiKeyAsync(HttpClient admin, string serviceAccountName)
    {
        var created = await PostFormAsync(admin, "/ServiceAccounts/Create", new()
        {
            ["Name"] = serviceAccountName,
            ["Clearance"] = ((int)ClearanceLevel.Standard).ToString(CultureInfo.InvariantCulture),
            ["ProjectKey"] = "GLOBAL",
        });
        var details = created.Headers.Location?.OriginalString
            ?? throw new InvalidOperationException($"Creating a service account answered {(int)created.StatusCode}, not a redirect.");

        var issueKey = details.Replace("/Details/", "/IssueKey/", StringComparison.OrdinalIgnoreCase);
        await PostFormAsync(admin, issueKey, new() { ["ttlDays"] = "0" }, formPage: details);

        var page = await admin.GetStringAsync(details);
        return IssuedApiKey().Match(page) is { Success: true } apiKey
            ? apiKey.Groups[1].Value
            : throw new InvalidOperationException("The service account page showed no newly issued API key.");
    }

    /// <summary>
    /// A new Standard-clearance vault account, created by the administrator and signed in through the
    /// real first sign-in. For tests that end their session: revoking one never touches the administrator's.
    /// Every client shares one address unless the test gives it a <paramref name="source"/> of its own,
    /// and the vault allows each address only a few sign-in steps per window.
    /// </summary>
    public async Task<VaultSession> SignInNewAccountAsync(
        string username, IPAddress? source = null, ClearanceLevel clearance = ClearanceLevel.Standard, string project = "GLOBAL")
    {
        var password = "It-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var created = await PostFormAsync(await AdminClientAsync(), "/Admin/CreateUser", new()
        {
            ["Username"] = username,
            ["FirstName"] = "Integration",
            ["LastName"] = "Test",
            ["Password"] = password,
            ["Clearance"] = ((int)clearance).ToString(CultureInfo.InvariantCulture),
            ["ProjectKey"] = project,
        });
        if (created.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException($"Creating the account {username} answered {(int)created.StatusCode}, not a redirect.");
        }

        var session = CreateSession(source: source);
        await SignInFirstTimeAsync(session.Client, username, password);
        return session;
    }

    /// <summary>
    /// A client with no session whose cookies the test can read, or seed with a copied session cookie.
    /// The test server has no network, so a <paramref name="source"/> stands in for the connection's address.
    /// </summary>
    public VaultSession CreateSession(Cookie? sessionCookie = null, IPAddress? source = null)
    {
        var cookies = new CookieContainer();
        if (sessionCookie is not null)
        {
            cookies.Add(BaseAddress, new Cookie(sessionCookie.Name, sessionCookie.Value, "/") { Secure = true, HttpOnly = true });
        }

        var handler = new CookieContainerHandler(cookies)
        {
            InnerHandler = _factory!.Server.CreateHandler(context => context.Connection.RemoteIpAddress ??= source),
        };
        return new VaultSession(new HttpClient(handler) { BaseAddress = BaseAddress }, cookies);
    }

    /// <summary>Revokes every session of an account the way an administrator does, from the Users page.</summary>
    public async Task RevokeAllSessionsOfAsync(string username)
    {
        var admin = await AdminClientAsync();
        var users = await admin.GetStringAsync("/Admin/Users");
        var forceLogout = ForceLogoutForm().Matches(users)
            .FirstOrDefault(m => WebUtility.HtmlDecode(m.Value).Contains($"Revoke all active sessions for '{username}'?", StringComparison.Ordinal))?.Groups[1].Value
            ?? throw new InvalidOperationException($"The Users page offered no way to revoke {username}'s sessions.");

        var response = await PostFormAsync(admin, forceLogout, new(), formPage: "/Admin/Users");
        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException($"Revoking {username}'s sessions answered {(int)response.StatusCode}, not a redirect.");
        }
    }

    /// <summary>
    /// Sends one anonymous GET from <paramref name="source"/>. The test server has no network, so the
    /// test supplies the address the vault would otherwise read from the connection.
    /// </summary>
    public Task<HttpContext> SendFromAsync(IPAddress source, string path) => _factory!.Server.SendAsync(context =>
    {
        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString(BaseAddress.Host);
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = source;
    });

    /// <summary>The vault's own services, for the few things a test cannot do through HTTP.</summary>
    public IServiceProvider Services => _factory!.Services;

    private sealed class AuthThrottleClock : TimeProvider
    {
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private async Task<HttpClient> SignInAdministratorAsync()
    {
        var client = CreateClient();
        await SignInFirstTimeAsync(client, AdminUsername, _adminPassword);
        return client;
    }

    /// <summary>
    /// The first sign-in of a new account: the password, then enrolling an authenticator and confirming
    /// it with a real code.
    /// </summary>
    private static async Task SignInFirstTimeAsync(HttpClient client, string username, string password)
    {
        var afterPassword = await PostFormAsync(client, "/Account/Login", new() { ["Username"] = username, ["Password"] = password });
        var next = afterPassword.Headers.Location?.OriginalString;
        if (!string.Equals(next, "/Account/EnrollTotp", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The password step ended at '{next}' ({(int)afterPassword.StatusCode}), not at authenticator enrollment.");
        }

        var enrollment = await client.GetStringAsync("/Account/EnrollTotp");
        var secret = EnrollmentSecret().Match(enrollment) is { Success: true } match
            ? match.Groups[1].Value
            : throw new InvalidOperationException("The enrollment page showed no authenticator secret.");

        // A code about to expire may reach the vault in the next step, so wait for a fresh one.
        if (Totp.SecondsLeftInStep() < 5)
        {
            await Task.Delay(TimeSpan.FromSeconds(Totp.SecondsLeftInStep() + 1));
        }

        var response = await PostFormAsync(client, "/Account/EnrollTotp", new() { ["Code"] = Totp.Code(secret, Totp.CurrentStep()) });
        var landed = response.Headers.Location?.OriginalString ?? string.Empty;
        if (response.StatusCode != HttpStatusCode.Redirect || landed.StartsWith("/Account/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Enrollment did not create a session ({(int)response.StatusCode}, '{landed}').");
        }
    }

    /// <summary>Posts a form as a browser does: with the antiforgery token from the page that renders it.</summary>
    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string path, Dictionary<string, string> fields, string? formPage = null)
    {
        fields["__RequestVerificationToken"] = FormTokenOf(await client.GetStringAsync(formPage ?? path), formPage ?? path);
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    /// <summary>The antiforgery token a Legacy UI page renders into its forms, where its own scripts read it.</summary>
    public static string FormTokenOf(string page, string path)
        => AntiforgeryField().Match(page) is { Success: true } match
            ? WebUtility.HtmlDecode(match.Groups[1].Value)
            : throw new InvalidOperationException($"{path} rendered no antiforgery token.");

    private void SetEnvironment(string name, string value)
    {
        _environment.Add(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryField();

    [GeneratedRegex("<code class=\"secret-code\">([A-Z2-7=]+)</code>")]
    private static partial Regex EnrollmentSecret();

    [GeneratedRegex("<code class=\"secret-code\" id=\"new-token\">([^<]+)</code>")]
    private static partial Regex IssuedApiKey();

    // The form tag helper writes action after the view's own attributes, so match the whole tag.
    [GeneratedRegex("<form\\b[^>]*\\baction=\"(/Admin/ForceLogout/[0-9a-fA-F-]{36})\"[^>]*>")]
    private static partial Regex ForceLogoutForm();
}

/// <summary>A client and the cookies it holds, so a test can inspect or copy its session cookie.</summary>
public sealed record VaultSession(HttpClient Client, CookieContainer Cookies)
{
    public Cookie? SessionCookie => Cookies.GetCookies(Client.BaseAddress!)[VaultFixture.SessionCookie];
}

/// <summary>Every integration test shares the one vault: starting SQL Server per test would take minutes.</summary>
[CollectionDefinition(Name)]
public sealed class VaultCollection : ICollectionFixture<VaultFixture>
{
    public const string Name = "vault";
}
