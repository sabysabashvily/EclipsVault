using System.Security.Claims;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure;
using EclipsVault.Infrastructure.Logging;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Workers;
using EclipsVault.Web.Authentication;
using EclipsVault.Infrastructure.Security;
using EclipsVault.Infrastructure.Security.Licensing;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Hosting;
using EclipsVault.Web.Middleware;
using EclipsVault.Web.Security;
using EclipsVault.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Serilog;

Log.Logger = SerilogSetup.CreateBootstrapLogger();

try
{
    Log.Information("Starting EclipsVault");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseEclipsVaultSerilog();
    builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

    // ---- Composition root -------------------------------------------------------

    builder.Services.AddEclipsVaultInfrastructure(builder.Configuration);
    builder.Services.AddHostedService<SecretLifecycleWorker>();
    builder.Services.AddHostedService<DynamicLeaseWorker>();

    // Registered after the audit committer (inside AddEclipsVaultInfrastructure) so it starts once the
    // committer is draining: it logs the license status and records one soft audit row if the vault
    // came up unlicensed outside Development. It never blocks startup.
    builder.Services.AddHostedService<LicenseStartupCheck>();

    // Precompute the licensing-banner inputs once from the single config-active feature source. Soft —
    // it only decides what the banner says; it never changes what the vault does.
    builder.Services.AddSingleton(sp =>
    {
        var license = sp.GetRequiredService<ILicenseState>();
        var active = sp.GetRequiredService<ConfiguredPremiumFeatures>().Active;
        return LicenseNudgeState.From(license, active);
    });

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IAuditContext, HttpAuditContext>();

    // The keys behind every auth cookie, antiforgery token and session. Must outlive the process
    // and be shared by every node, or users are signed out by a restart and by each other's replicas.
    builder.Services.AddVaultDataProtection(builder.Configuration, builder.Environment);

    // ABAC: policy-based authorization with a resource-aware handler.
    builder.Services.Configure<AbacOptions>(builder.Configuration.GetSection(AbacOptions.SectionName));
    builder.Services.AddScoped<IAccessContextProvider, AccessContextProvider>();
    builder.Services.AddScoped<IAuthorizationHandler, SecretAccessHandler>();

    builder.Services
        .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Cookie.Name = "EclipsVault.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/Denied";
            options.ExpireTimeSpan = SessionDefaults.InteractiveLifetime;
            options.SlidingExpiration = true;
            var cookieDefaults = new CookieAuthenticationEvents();
            options.Events = new CookieAuthenticationEvents
            {
                // Server-side kill switch: sessions revoked by the intrusion response
                // die on their next request, regardless of cookie lifetime.
                OnValidatePrincipal = async context =>
                {
                    var revocation = context.HttpContext.RequestServices.GetRequiredService<ISessionRevocationService>();

                    var userIdClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                    var authTimeClaim = context.Principal?.FindFirstValue(VaultClaimTypes.AuthTime);

                    var isValid = false;
                    if (Guid.TryParse(userIdClaim, out var userId) &&
                        long.TryParse(authTimeClaim, out var authTimeUnix))
                    {
                        // Account-wide kill switch: sessions issued at or before a revocation instant die.
                        isValid = !await revocation.IsRevokedAsync(userId, DateTimeOffset.FromUnixTimeSeconds(authTimeUnix));

                        // In-memory revocation is lost on restart, but account removal is durable.
                        // A decryptable cookie must still belong to an existing, enabled account.
                        if (isValid)
                        {
                            var users = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
                            isValid = await users.FindByIdAsync(userId, context.HttpContext.RequestAborted)
                                is { IsDisabled: false };
                        }

                        // Per-session kill switch + activity tracking, for sessions that carry a session id.
                        if (isValid && Guid.TryParse(context.Principal?.FindFirstValue(VaultClaimTypes.SessionId), out var sessionId))
                        {
                            var registry = context.HttpContext.RequestServices.GetRequiredService<ISessionRegistry>();
                            if (await registry.IsRevokedAsync(userId, sessionId))
                            {
                                isValid = false;
                            }
                            else
                            {
                                // Last-seen is best-effort metadata — never fail a valid session over it.
                                try
                                {
                                    var now = DateTimeOffset.UtcNow;
                                    await registry.RecordSeenAsync(new SessionObservation(
                                        userId,
                                        sessionId,
                                        UserAgentSummary.Describe(context.Request.Headers.UserAgent.ToString()),
                                        context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                                        now,
                                        now + SessionDefaults.InteractiveLifetime));
                                }
                                catch (Exception ex)
                                {
                                    context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                                        .CreateLogger("SessionRegistry")
                                        .LogDebug(ex, "Non-fatal: could not record session activity");
                                }
                            }
                        }
                    }

                    if (!isValid)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                },

                // The UI API answers with Problem Details, not a redirect to an HTML page (ADR-0006).
                // A missing or revoked session is a 401 the Angular UI turns into a trip to sign-in.
                // Every other path keeps the framework's behaviour.
                OnRedirectToLogin = context => UiApiProblem.IsUiApi(context.Request)
                    ? UiApiProblem.UnauthenticatedAsync(context.HttpContext)
                    : cookieDefaults.RedirectToLogin(context),
                OnRedirectToAccessDenied = context => UiApiProblem.IsUiApi(context.Request)
                    ? UiApiProblem.ForbiddenAsync(context.HttpContext)
                    : cookieDefaults.RedirectToAccessDenied(context),
            };
        })
        .AddCookie(AuthSchemes.MfaPending, options =>
        {
            options.Cookie.Name = "EclipsVault.MfaPending";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.LoginPath = "/Account/Login";
            options.ExpireTimeSpan = TimeSpan.FromMinutes(6);
            options.SlidingExpiration = false;
        })
        .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(AuthSchemes.ApiKey, _ => { });

    // Single sign-on — only wired up when configured, so a deployment that does not use it carries
    // none of its surface. The handler proves who the caller is at the IdP and nothing more: the
    // vault decides whether they may in (see SsoSignInService), which is why this signs into a
    // throwaway correlation scheme rather than the session.
    var sso = builder.Configuration.GetSection(SsoOptions.SectionName).Get<SsoOptions>() ?? new SsoOptions();
    if (sso.Enabled)
    {
        builder.Services
            .AddAuthentication()
            .AddCookie(AuthSchemes.OidcCorrelation, options =>
            {
                options.Cookie.Name = "EclipsVault.OidcCorrelation";
                options.Cookie.HttpOnly = true;
                // Lax, not Strict: the IdP redirects back with a cross-site POST/GET, and Strict
                // would withhold the correlation cookie and fail every sign-in.
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
            })
            .AddOpenIdConnect(AuthSchemes.Oidc, options =>
            {
                options.Authority = sso.Authority;
                options.ClientId = sso.ClientId;
                options.ClientSecret = sso.ClientSecret;
                options.SignInScheme = AuthSchemes.OidcCorrelation;

                // Authorization code with PKCE, and no tokens in the front channel.
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.UsePkce = true;
                options.ResponseMode = OpenIdConnectResponseMode.FormPost;

                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");

                options.GetClaimsFromUserInfoEndpoint = true;
                options.SaveTokens = false; // the vault has no use for the IdP's tokens after sign-in
                options.MapInboundClaims = false; // keep the raw OIDC claim names (sub, amr, email_verified)

                // The handler drops these as protocol noise by default. They are not noise here:
                // 'iss' is *which* IdP made the assertion, without which the trail cannot say whose
                // word was taken, and 'amr' is how they authenticated, which decides whether the
                // vault's own second factor may be waived.
                options.ClaimActions.Remove("iss");
                options.ClaimActions.Remove("amr");

                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidateLifetime = true;

                // Discovery over plain HTTP means the JWKS can be swapped in flight, and with it the
                // keys that validate every token. Dev-only, and it has to be asked for.
                options.RequireHttpsMetadata = !sso.AllowInsecureHttp;
            });
    }

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy(VaultPolicies.Session, policy => policy.RequireAuthenticatedUser());

        options.AddPolicy(VaultPolicies.SecretAccess, policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new SecretAccessRequirement()));

        options.AddPolicy(VaultPolicies.AdminOnly, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(VaultClaimTypes.Clearance, ((int)ClearanceLevel.TopSecret).ToString()));

        // Default-deny: everything requires an authenticated session unless
        // explicitly marked [AllowAnonymous].
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    });

    // Brute-force damping on the authentication surface, partitioned per source IP. Applied by
    // AuthThrottleFilter on AccountController; the budget itself lives behind IAuthThrottle so it
    // can be shared by every replica (Redis) instead of granting each one its own (in-process).
    builder.Services.AddScoped<AuthThrottleFilter>();

    // Recover the real client IP behind a reverse proxy / load balancer. Without this,
    // every IP-based control — the auth rate limiter, the intrusion IP-blacklist, the
    // ABAC trusted-network check, and the audit SourceIp — would see the proxy's address
    // instead of the caller's. Forwarded headers are honoured ONLY from explicitly
    // configured proxies; with none configured the socket address is used (safe default).
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = builder.Configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 1;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        {
            if (System.Net.IPAddress.TryParse(proxy, out var ip))
            {
                options.KnownProxies.Add(ip);
            }
        }
    });

    builder.Services.AddControllersWithViews(options =>
    {
        // CSRF: every unsafe verb on every controller validates the antiforgery token.
        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
        options.Filters.Add(new UiApiAntiforgeryFilter());
    });

    builder.Services.AddAntiforgery(options =>
    {
        options.Cookie.Name = "EclipsVault.Csrf";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        // Lets the passkey fetch() calls present the token as a header (their body is JSON).
        options.HeaderName = "RequestVerificationToken";
    });

    // HSTS: pin browsers to HTTPS for a credentials product. The framework default is a
    // 30-day max-age with no subdomain coverage; we harden it to a year, extend it to every
    // subdomain, and opt into the browser preload list so the very first request is already
    // forced onto TLS. Emitted by UseHsts() below (production only, never on localhost).
    builder.Services.AddHsts(options =>
    {
        options.MaxAge = TimeSpan.FromDays(365);
        options.IncludeSubDomains = true;
        options.Preload = true;
    });

    // Server-side ceremony state for WebAuthn: the issued challenge is held here between the
    // "begin" and "complete" calls, so it can never be tampered with by the client.
    builder.Services.AddSession(options =>
    {
        options.Cookie.Name = "EclipsVault.Ceremony";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.IsEssential = true;
        options.IdleTimeout = TimeSpan.FromMinutes(6);
    });

    // ---- Pipeline ----------------------------------------------------------------

    var app = builder.Build();

    // Must run before anything reads the client IP (request logging, blacklist, ABAC).
    app.UseForwardedHeaders();

    app.UseSerilogRequestLogging();
    app.UseMiddleware<GlobalExceptionMiddleware>();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();

        // Host filtering only binds when AllowedHosts names real hosts; "*" (the framework default)
        // waves through any Host header. Same spirit as the KEK dev-fallback warning: the app still
        // runs, but a real deployment should pin this to the vault's own hostname(s) so a spoofed or
        // reflected Host cannot slip through. Left permissive by default because the code cannot know
        // the deployment's hostname — this is the one place to say so out loud.
        var allowedHosts = app.Configuration["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(allowedHosts) ||
            allowedHosts.Split(';').Any(host => host.Trim() == "*"))
        {
            app.Logger.LogWarning(
                "AllowedHosts is '{AllowedHosts}' outside Development — set it to this vault's hostname(s) so Host-header filtering is actually enforced",
                allowedHosts ?? "(unset)");
        }
    }

    app.UseHttpsRedirection();
    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseStaticFiles();
    app.UseMiddleware<IpBlacklistMiddleware>();
    app.UseRouting();
    app.UseSession();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapAngularUi();

    // An unknown UI API path is a Problem Details not-found like any other UI API failure (ADR-0006).
    // It still needs a session: the fallback authorization policy applies to it.
    app.MapFallback("/api/ui/{**path}", UiApiProblem.NotFoundAsync);

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

    // Fail fast if the crypto subsystem is misconfigured (missing/invalid KEK).
    using (var scope = app.Services.CreateScope())
    {
        _ = scope.ServiceProvider.GetRequiredService<ICryptoEngineFactory>().Create();
    }

    await DatabaseMigrator.MigrateAsync(app.Services, app.Environment);
    await DbSeeder.SeedAsync(app.Services, app.Environment);

    // Adopt the payload-to-row binding on a vault written before it existed. A no-op with nothing
    // to upgrade; otherwise it says what is wrong and how to fix it rather than letting each read
    // fail on its own.
    await LegacyBlobUpgrader.UpgradeAsync(app.Services);

    // Back-fill + seed the audit hash chain (after migrations + seeding are in place).
    await AuditChainInitializer.InitializeAsync(app.Services);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "EclipsVault terminated unexpectedly");

    // A refused startup has to look like a failure to whatever is supervising this process.
    // Exiting 0 tells an orchestrator, a deploy job, or a shell checking $? that the vault came
    // up healthy — so the checks that deliberately stop the boot (no bootstrap password, an
    // unreadable KEK, a schema behind its migrations) would be reported as success.
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}
