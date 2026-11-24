using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog.Core;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// One real vault per test run: the unmodified <c>Program</c> entry point, in Production, against a
/// SQL Server or PostgreSQL container, whichever the run chose (ADR-0010, <see cref="VaultDatabase"/>).
/// Authentication and persistence are real. It is configured the way a container
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
    internal const string SqlServerImage =
        "mcr.microsoft.com/mssql/server:2025-latest@sha256:ca62ad2d75975074cbc9ac39a1329cd88af9cd3ea2ffb59d6302579a97cf32f4";

    public const string AdminUsername = "vault-admin";

    public const string SessionCookie = "EclipsVault.Session";

    /// <summary>The one reverse proxy whose forwarded headers the vault honours, as an operator configures it.</summary>
    public static readonly IPAddress ForwardingProxy = IPAddress.Parse("198.18.114.254");

    private static readonly Uri BaseAddress = new("https://localhost");

    private readonly VaultDatabase _database = VaultDatabase.ForThisRun();

    private readonly DirectoryInfo _keyRing = Directory.CreateTempSubdirectory("eclipsvault-it-keys-");
    private readonly List<string> _environment = [];

    /// <summary>The bootstrap administrator's password, chosen per run as an operator would.</summary>
    private readonly string _adminPassword = "It-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private WebApplicationFactory<Program>? _factory;
    private Task<HttpClient>? _adminClient;
    private string? _adminAuthenticatorSecret;
    private DateTimeOffset _adminAuthenticatedAt;
    private readonly SemaphoreSlim _administratorProof = new(1, 1);
    private int _activationSources;

    public bool TrustIdpMfa { get; init; }

    /// <summary>
    /// The SMTP capture the vault delivers to. Every vault has one unless a test sets it to null, because new
    /// accounts are activated through the invitation it receives (ADR-0033).
    /// </summary>
    public SmtpCapture? Mailbox { get; init; } = new SmtpCapture();

    /// <summary>The address invitation links are built from; null leaves invitations unavailable.</summary>
    public string? PublicUrl { get; init; } = "https://vault.example.test";

    public string? RedisConfiguration { get; init; }

    public bool ProductionWindowNeverOpens { get; init; }

    /// <summary>A deployment without single sign-on; every other vault offers it.</summary>
    public bool SsoDisabled { get; init; }

    /// <summary>The token the vault reads from ECLIPSVAULT_LICENSE at startup; none means unlicensed.</summary>
    public string? License { get; init; }

    /// <summary>A master key the vault loads only to unwrap data keys still sealed under it.</summary>
    public string? RetiredMasterKey { get; init; }

    /// <summary>Gives the vault a SQL Server database of its own to mint dynamic credentials on.</summary>
    public bool DynamicSecretsTarget { get; init; }

    /// <summary>The database dynamic credentials are minted on, when <see cref="DynamicSecretsTarget"/> is set.</summary>
    public string? DynamicTargetConnectionString { get; private set; }

    /// <summary>Gives the vault a PostgreSQL database of its own to mint <c>Postgres</c> roles' credentials on.</summary>
    public bool PostgresDynamicSecretsTarget { get; init; }

    /// <summary>The database <c>Postgres</c> roles mint on, when <see cref="PostgresDynamicSecretsTarget"/> is set.</summary>
    public string? PostgresDynamicTargetConnectionString { get; private set; }

    /// <summary>Every line the vault logs, as its log sinks receive it.</summary>
    public LogCapture Log { get; } = new();

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        if (Mailbox is not null)
        {
            await Mailbox.StartAsync();
            SetEnvironment("Email__Sender", "Smtp");
            SetEnvironment("Email__Enabled", "true");
            SetEnvironment("Email__Smtp__Host", Mailbox.Host);
            SetEnvironment("Email__Smtp__Port", Mailbox.Port.ToString(CultureInfo.InvariantCulture));
        }
        if (DynamicSecretsTarget)
        {
            DynamicTargetConnectionString = await _database.StartDynamicTargetAsync();
            SetEnvironment("DynamicSecrets__TargetConnectionString", DynamicTargetConnectionString);
        }
        if (PostgresDynamicSecretsTarget)
        {
            PostgresDynamicTargetConnectionString = await _database.StartPostgresDynamicTargetAsync();
            SetEnvironment("DynamicSecrets__PostgresTargetConnectionString", PostgresDynamicTargetConnectionString);
        }

        if (PublicUrl is not null)
        {
            SetEnvironment("Identity__PublicUrl", PublicUrl);
        }

        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SetEnvironment("Database__Provider", _database.Provider);
        SetEnvironment("ConnectionStrings__DefaultConnection", _database.ConnectionString());
        SetEnvironment("ECLIPSVAULT_KEK", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        if (RetiredMasterKey is not null) SetEnvironment("ECLIPSVAULT_KEK_RETIRED", RetiredMasterKey);
        SetEnvironment("ECLIPSVAULT_AUDIT_SIGNING_KEY", Convert.ToBase64String(signingKey.ExportPkcs8PrivateKey()));
        SetEnvironment("DataProtection__KeyRingPath", _keyRing.FullName);
        SetEnvironment("Seed__AdminPassword", _adminPassword);
        SetEnvironment("ForwardedHeaders__KnownProxies__0", ForwardingProxy.ToString());
        if (RedisConfiguration is not null)
        {
            SetEnvironment("Redis__Enabled", "true");
            SetEnvironment("Redis__Configuration", RedisConfiguration + ",asyncTimeout=1000,syncTimeout=1000");
            SetEnvironment("Redis__AllowUnauthenticated", "true");
            SetEnvironment("Redis__SessionIncarnation", "before-recovery");
        }
        // The single-node install with no deploy pipeline, which lets the vault apply its own migrations.
        SetEnvironment("Database__MigrateOnStartup", "true");
        // The secret and lease sweeps write audit rows on their own timers. In a shared vault they land
        // inside other tests' audit windows, so they wait a day; no HTTP test relies on them.
        SetEnvironment("Lifecycle__SweepIntervalSeconds", "86400");
        SetEnvironment("DynamicSecrets__ReapIntervalSeconds", "86400");
        // Register the production OIDC correlation cookie for HTTP callback scoping tests.
        // Those start after the IdP assertion, so this authority is never contacted.
        SetEnvironment("Sso__Enabled", SsoDisabled ? "false" : "true");
        SetEnvironment("Sso__TrustIdpMultiFactor", TrustIdpMfa.ToString());
        SetEnvironment("Sso__Authority", "https://idp.example.test/realms/integration");
        SetEnvironment("Sso__ClientId", "integration-vault");
        SetEnvironment("Sso__ClientSecret", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        if (License is not null) SetEnvironment("ECLIPSVAULT_LICENSE", License);
        if (ProductionWindowNeverOpens)
        {
            // The window runs from the start hour up to the end hour, so equal hours never open it.
            SetEnvironment("Abac__ProductionWindowStartUtcHour", "0");
            SetEnvironment("Abac__ProductionWindowEndUtcHour", "0");
        }

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host => host
                .UseEnvironment("Production")
                .ConfigureTestServices(services =>
                {
                    // TestServer has no listening HTTPS socket for the redirect middleware to discover.
                    services.Configure<HttpsRedirectionOptions>(options => options.HttpsPort = 443);
                    services.AddMvcCore().AddApplicationPart(typeof(VaultFixture).Assembly);
                    services.AddSingleton<ILogEventSink>(Log);
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

        // A run on one engine must never pass while the vault quietly uses the other.
        using var scope = services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<EclipsVaultDbContext>().Database.IsNpgsql()
            ? DatabaseProvider.Postgres
            : DatabaseProvider.SqlServer;
        if (engine != _database.Provider)
        {
            throw new InvalidOperationException($"The vault under test runs on {engine}, not {_database.Provider}.");
        }
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _database.DisposeAsync();
        if (Mailbox is not null)
        {
            await Mailbox.DisposeAsync();
        }
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

    public Task<HttpContext> SendAsync(Action<HttpContext> configure) => _factory!.Server.SendAsync(configure);

    /// <summary>
    /// The real bootstrap session, keeping its proof fresh through the same step-up endpoint as the UI.
    /// Slow suites can outlast a proof window; explicitly stale sessions are built separately.
    /// </summary>
    public async Task<HttpClient> AdminClientAsync()
    {
        var client = await (_adminClient ??= SignInAdministratorAsync());
        await _administratorProof.WaitAsync();
        try
        {
            var refreshInterval = TimeSpan.FromMinutes(5);
            if (DateTimeOffset.UtcNow - _adminAuthenticatedAt < refreshInterval)
            {
                return client;
            }

            if (Totp.SecondsLeftInStep() < 5)
            {
                await Task.Delay(TimeSpan.FromSeconds(Totp.SecondsLeftInStep() + 1));
            }
            var secret = _adminAuthenticatorSecret ?? throw new InvalidOperationException("The administrator did not enroll an authenticator.");
            var response = await UiApiRequest.SendWithTokenAsync(client, await UiApiRequest.AntiforgeryTokenAsync(client),
                HttpMethod.Post, "/api/ui/step-up", JsonContent.Create(new { code = Totp.Code(secret, Totp.CurrentStep()) }));
            response.EnsureSuccessStatusCode();
            _adminAuthenticatedAt = DateTimeOffset.UtcNow;
            return client;
        }
        finally
        {
            _administratorProof.Release();
        }
    }

    /// <summary>Creates a service account and reads its one-time key from the UI API issuance response.</summary>
    public async Task<string> IssueApiKeyAsync(HttpClient admin, string serviceAccountName)
    {
        var created = await ServiceAccountsApi.WriteAsync(admin, HttpMethod.Post, ServiceAccountsApi.Path,
            JsonContent.Create(new { name = serviceAccountName, clearance = 1, project = "GLOBAL" }));
        created.EnsureSuccessStatusCode();
        var account = await created.Content.ReadFromJsonAsync<EclipsVault.Web.Models.Ui.CreatedServiceAccountResponse>()
            ?? throw new InvalidOperationException("Service-account creation returned no response.");
        return (await ServiceAccountsApi.IssuedAsync(admin, account.ServiceAccountId)).ApiKey;
    }

    /// <summary>
    /// A new Standard-clearance vault account, invited by the administrator, activated through the emailed
    /// link with <paramref name="password"/> and a new authenticator, then signed in with both. For tests that
    /// end their session: revoking one never touches the administrator's. Every client shares one address
    /// unless the test gives it a <paramref name="source"/> of its own, and the vault allows each address only
    /// a few sign-in steps per window; activation uses an address of its own, so it spends none of them.
    /// </summary>
    public async Task<VaultSession> SignInNewAccountAsync(
        string username, IPAddress? source = null, ClearanceLevel clearance = ClearanceLevel.Standard, string project = "GLOBAL",
        string? password = null)
    {
        password ??= "It-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var created = await UserAccounts.CreateAsync(await AdminClientAsync(), username, clearance: clearance, project: project);
        created.EnsureSuccessStatusCode();

        // Activation spends the step before the vault's current one and the sign-in the current one, as an
        // enrollment at first sign-in did, so a test's next code is the next step's. Both are chosen by the
        // vault's clock and its record of the spent step.
        var vaultClock = Services.GetRequiredService<TimeProvider>();
        var step = await VaultStepWithTimeLeftAsync(vaultClock);
        var activationSource = NextActivationSource();
        var secret = await ActivateFromAsync(UserAccounts.EmailOf(username), password, step - 1, activationSource);
        var activated = await ProfileHttp.AccountAsync(this, username);
        var session = CreateSession(source: source);
        await SignInWithAuthenticatorAsync(session.Client, username, password,
            Totp.Code(secret, Totp.StepStillAccepted(vaultClock.GetUtcNow(), activated.LastTotpStep)));
        session.ProfileOwner = new ProfileAccount(session, await ProfileHttp.AccountAsync(this, username), password);
        session.ActivationSource = activationSource;
        return session;
    }

    /// <summary>
    /// The vault's current step. Three codes fit one step (the one before, this one and the next), and the vault
    /// accepts the one before only until this step ends, so a step about to end is waited out on the vault's clock.
    /// </summary>
    private static async Task<long> VaultStepWithTimeLeftAsync(TimeProvider vaultClock)
    {
        var now = vaultClock.GetUtcNow();
        var left = TimeSpan.FromSeconds(30) - TimeSpan.FromMilliseconds(now.ToUnixTimeMilliseconds() % 30_000);
        if (left < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(left + TimeSpan.FromMilliseconds(100), vaultClock);
            now = vaultClock.GetUtcNow();
        }

        return Totp.StepAt(now);
    }

    /// <summary>
    /// Activates the invited account at <paramref name="email"/> through the latest invitation the mailbox
    /// received: inspects the link's token, then chooses <paramref name="password"/> and confirms the shown
    /// authenticator with its code for <paramref name="step"/>. Returns the authenticator secret. Each call
    /// comes from an address of its own, so it spends no test's sign-in budget.
    /// </summary>
    public Task<string> ActivateAsync(string email, string password, long step)
        => ActivateFromAsync(email, password, step, NextActivationSource());

    private async Task<string> ActivateFromAsync(string email, string password, long step, IPAddress source)
    {
        var mailbox = Mailbox ?? throw new InvalidOperationException("Activating an account needs the vault's mailbox.");
        var (_, _, _, token) = await mailbox.LatestInvitationForAsync(email);
        var client = CreateSession(source: source).Client;

        var inspected = await UiApiRequest.SendWithTokenAsync(client, await SignInDocument.TokenAsync(client), HttpMethod.Post,
            "/api/ui/auth/activation", JsonContent.Create(new { token }));
        var secret = inspected.IsSuccessStatusCode
            ? (await inspected.Content.ReadFromJsonAsync<EclipsVault.Web.Models.Ui.ActivationResponse>())?.SecretBase32
            : null;
        if (secret is null)
        {
            throw new InvalidOperationException($"The invitation for {email} showed no authenticator secret ({(int)inspected.StatusCode}).");
        }

        var completed = await UiApiRequest.SendWithTokenAsync(client, await SignInDocument.TokenAsync(client), HttpMethod.Post,
            "/api/ui/auth/activation/complete", JsonContent.Create(new { token, password, code = Totp.Code(secret, step) }));
        if (completed.StatusCode != HttpStatusCode.NoContent)
        {
            throw new InvalidOperationException($"Activating {email} did not complete ({(int)completed.StatusCode}).");
        }

        return secret;
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
        var users = await admin.GetFromJsonAsync<EclipsVault.Web.Models.Ui.UsersResponse>("/api/ui/users");
        var account = users!.Users.Single(user => user.Username == username);
        (await UserActions.ForceLogoutAsync(admin, account.UserId)).EnsureSuccessStatusCode();
    }

    /// <summary>Deletes an account through the Users UI API.</summary>
    public async Task DeleteAccountAsync(string username)
    {
        var admin = await AdminClientAsync();
        var users = await admin.GetFromJsonAsync<EclipsVault.Web.Models.Ui.UsersResponse>("/api/ui/users");
        var account = users!.Users.Single(user => user.Username == username);
        (await UserActions.DeleteAsync(admin, account.UserId)).EnsureSuccessStatusCode();
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
        _adminAuthenticatorSecret = await SignInFirstTimeAsync(client, AdminUsername, _adminPassword);
        _adminAuthenticatedAt = DateTimeOffset.UtcNow;
        return client;
    }

    /// <summary>
    /// The first sign-in of a new account through the Angular sign-in pages' calls: the password, then
    /// enrolling an authenticator and confirming it with a real code.
    /// </summary>
    private static async Task<string> SignInFirstTimeAsync(HttpClient client, string username, string password)
    {
        var afterPassword = await UiApiRequest.SendWithTokenAsync(client, await SignInDocument.TokenAsync(client), HttpMethod.Post,
            "/api/ui/auth/password", JsonContent.Create(new { username, password }));
        var next = afterPassword.IsSuccessStatusCode
            ? (await afterPassword.Content.ReadFromJsonAsync<EclipsVault.Web.Models.Ui.PasswordStageResponse>())?.Next
            : null;
        if (next != "enroll")
        {
            throw new InvalidOperationException(
                $"The password step went to '{next}' ({(int)afterPassword.StatusCode}), not to authenticator enrollment.");
        }

        // The password step reissued the token for the pending sign-in.
        var pending = SignInDocument.TokenOf(afterPassword);
        var enrollment = await UiApiRequest.SendWithTokenAsync(client, pending, HttpMethod.Post, "/api/ui/auth/enrollment");
        var secret = enrollment.IsSuccessStatusCode
            ? (await enrollment.Content.ReadFromJsonAsync<EclipsVault.Web.Models.Ui.TotpEnrollmentResponse>())?.SecretBase32
            : null;
        if (secret is null)
        {
            throw new InvalidOperationException($"Enrollment showed no authenticator secret ({(int)enrollment.StatusCode}).");
        }

        // A code about to expire may reach the vault in the next step, so wait for a fresh one.
        if (Totp.SecondsLeftInStep() < 5)
        {
            await Task.Delay(TimeSpan.FromSeconds(Totp.SecondsLeftInStep() + 1));
        }

        var response = await UiApiRequest.SendWithTokenAsync(client, pending, HttpMethod.Post, "/api/ui/auth/enrollment/confirm",
            JsonContent.Create(new { code = Totp.Code(secret, Totp.CurrentStep()) }));
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Enrollment did not create a session ({(int)response.StatusCode}).");
        }
        return secret;
    }

    /// <summary>A sign-in of an activated account through the Angular sign-in pages' calls: the password, then its authenticator code.</summary>
    private static async Task SignInWithAuthenticatorAsync(HttpClient client, string username, string password, string code)
    {
        var afterPassword = await UiApiRequest.SendWithTokenAsync(client, await SignInDocument.TokenAsync(client), HttpMethod.Post,
            "/api/ui/auth/password", JsonContent.Create(new { username, password }));
        var next = afterPassword.IsSuccessStatusCode
            ? (await afterPassword.Content.ReadFromJsonAsync<EclipsVault.Web.Models.Ui.PasswordStageResponse>())?.Next
            : null;
        if (next != "totp")
        {
            throw new InvalidOperationException(
                $"The password step went to '{next}' ({(int)afterPassword.StatusCode}), not to the authenticator code.");
        }

        var response = await UiApiRequest.SendWithTokenAsync(client, SignInDocument.TokenOf(afterPassword), HttpMethod.Post,
            "/api/ui/auth/totp", JsonContent.Create(new { code }));
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"The authenticator code did not create a session ({(int)response.StatusCode}).");
        }
    }

    private IPAddress NextActivationSource()
    {
        var n = Interlocked.Increment(ref _activationSources);
        return new IPAddress([100, 120, (byte)(n / 250), (byte)(n % 250 + 1)]);
    }

    private void SetEnvironment(string name, string value)
    {
        _environment.Add(name);
        Environment.SetEnvironmentVariable(name, value);
    }



}

/// <summary>A client and the cookies it holds, so a test can inspect or copy its session cookie.</summary>
public sealed record VaultSession(HttpClient Client, CookieContainer Cookies)
{
    internal ProfileAccount? ProfileOwner { get; set; }
    internal IPAddress? ActivationSource { get; set; }
    internal long LastProfileProofStep { get; set; }

    public Cookie? SessionCookie => Cookies.GetCookies(Client.BaseAddress!)[VaultFixture.SessionCookie];
}

/// <summary>Ordinary HTTP tests share one vault: starting a database server per test would take minutes.</summary>
[CollectionDefinition(Name)]
public sealed class VaultCollection : ICollectionFixture<VaultFixture>
{
    public const string Name = "vault";
}
