using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Sso;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Auditing;
using EclipsVault.Infrastructure.Caching;
using EclipsVault.Infrastructure.Distributed;
using EclipsVault.Infrastructure.Media;
using EclipsVault.Infrastructure.Notifications;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Persistence.Interceptors;
using EclipsVault.Infrastructure.Persistence.Locking;
using EclipsVault.Infrastructure.Persistence.Repositories;
using EclipsVault.Infrastructure.Security;
using EclipsVault.Infrastructure.Security.Licensing;
using EclipsVault.Infrastructure.Security.WebAuthn;
using EclipsVault.Infrastructure.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EclipsVault.Infrastructure;

/// <summary>Registers every Infrastructure implementation behind its Core interface.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddEclipsVaultInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CryptoOptions>(configuration.GetSection(CryptoOptions.SectionName));
        services.Configure<Argon2Options>(configuration.GetSection(Argon2Options.SectionName));
        services.Configure<CacheOptions>(configuration.GetSection(CacheOptions.SectionName));
        services.Configure<LifecycleOptions>(configuration.GetSection(LifecycleOptions.SectionName));
        services.Configure<DynamicLeaseOptions>(configuration.GetSection(DynamicLeaseOptions.SectionName));
        services.Configure<AuthThrottleOptions>(configuration.GetSection(AuthThrottleOptions.SectionName));
        services.Configure<IntrusionResponseOptions>(configuration.GetSection(IntrusionResponseOptions.SectionName));
        services.Configure<WebAuthnOptions>(configuration.GetSection(WebAuthnOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<AuditSigningOptions>(configuration.GetSection(AuditSigningOptions.SectionName));
        services.Configure<SsoOptions>(configuration.GetSection(SsoOptions.SectionName));
        services.Configure<LicenseOptions>(configuration.GetSection(LicenseOptions.SectionName));
        services.Configure<DynamicSecretTargetOptions>(configuration.GetSection(DynamicSecretTargetOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();

        // Brute-force lockout thresholds (Core policy record, bound from configuration).
        var lockout = configuration.GetSection("Lockout");
        services.AddSingleton(new LockoutPolicy(
            lockout.GetValue<int?>("MaxFailedAttempts") ?? LockoutPolicy.Default.MaxFailedAttempts,
            TimeSpan.FromMinutes(lockout.GetValue<int?>("LockoutMinutes") ?? LockoutPolicy.Default.LockoutDuration.TotalMinutes)));

        // Step-up re-authentication policy (Core options, bound from configuration).
        var stepUp = configuration.GetSection(StepUpOptions.SectionName);
        services.AddSingleton(new StepUpOptions
        {
            MinimumSensitivity = stepUp.GetValue<SensitivityLevel?>("MinimumSensitivity") ?? new StepUpOptions().MinimumSensitivity,
            MaxAuthAgeMinutes = stepUp.GetValue<int?>("MaxAuthAgeMinutes") ?? new StepUpOptions().MaxAuthAgeMinutes
        });
        services.AddScoped<IStepUpService, StepUpService>();
        services.AddScoped<SecondFactorVerifier>();

        // Email domain used when auto-generating account emails.
        services.AddSingleton(new UserDirectoryOptions(
            configuration.GetValue<string?>("Identity:EmailDomain") ?? UserDirectoryOptions.Default.EmailDomain));

        // Persistence — the audit interceptor rides inside every SaveChanges, stamping each
        // audit row into the tamper-evidence hash chain (one shared, serialized chain head).
        services.AddSingleton<AuditChain>();
        services.AddScoped<AuditSaveChangesInterceptor>();

        // The connection string is deliberately absent from appsettings.json so credentials
        // are never committed. It must be supplied by the environment (ConnectionStrings__DefaultConnection)
        // or a secret store; the local dev value lives in appsettings.Development.json.
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Database connection string 'DefaultConnection' is not configured. Set the " +
                "ConnectionStrings__DefaultConnection environment variable (or a secret store).");

        // Which database to run on. SQL Server stays the default so existing deployments are
        // unaffected; PostgreSQL exists because a paid database is a line item on every self-hosted
        // deployment, and the vault should not be the reason for one.
        var provider = configuration.GetValue<string?>("Database:Provider") ?? DatabaseProvider.SqlServer;

        services.AddDbContext<EclipsVaultDbContext>((sp, options) => options
            .UseVaultDatabase(provider, connectionString)
            .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));

        // The one database-specific thing in the chain: the lock that serialises appends to it.
        services.AddSingleton<IAuditChainLocker>(_ => DatabaseProvider.IsPostgres(provider)
            ? new PostgresAuditChainLocker()
            : new SqlServerAuditChainLocker());

        // The one place standalone audit rows are written (fail-closed). It commits through the
        // group committer, which is why the same instance is both a singleton and the hosted
        // service: the queue and the loop draining it are one object.
        services.AddSingleton<AuditGroupCommitter>();
        services.AddHostedService(sp => sp.GetRequiredService<AuditGroupCommitter>());
        services.AddScoped<IAuditSink, AuditSink>();
        services.AddScoped<IAccountChangeCommitter, AccountChangeCommitter>();
        services.AddScoped<AccountTransaction>();
        services.AddScoped<IAuthenticationAttempt, AuthenticationAttempt>();
        services.AddScoped<AuthenticationFailures>();

        services.AddScoped<ISecretRepository, SecretRepository>();
        services.AddScoped<ISecretGrantRepository, SecretGrantRepository>();
        services.AddScoped<IServiceAccountRepository, ServiceAccountRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IPasskeyCredentialRepository, PasskeyCredentialRepository>();
        services.AddScoped<IMfaRecoveryCodeRepository, MfaRecoveryCodeRepository>();
        services.AddScoped<IAccessRequestRepository, AccessRequestRepository>();
        services.AddScoped<IEmailLogRepository, EmailLogRepository>();
        services.AddScoped<ITrustedNetworkRepository, TrustedNetworkRepository>();
        services.AddScoped<IDynamicSecretRepository, DynamicSecretRepository>();
        services.AddScoped<IProfileOperationRepository, ProfileOperationRepository>();

        // Security primitives.
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.AddSingleton<IBreachedPasswordScreen, BundledBreachedPasswordScreen>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<IAvatarProcessor, SkiaAvatarProcessor>();
        services.AddSingleton<IApiKeyFactory, ApiKeyFactory>();
        services.AddSingleton<IKekProvider, EnvironmentKekProvider>();
        services.AddSingleton<AesGcmCryptoEngine>();
        // Opt-in KMS engine: constructed lazily (and only reaches Vault) when Crypto:Engine=VaultTransit.
        services.Configure<VaultOptions>(configuration.GetSection(VaultOptions.SectionName));
        services.AddSingleton(sp => new VaultTransitCryptoEngine(
            new HttpClient(),
            sp.GetRequiredService<IOptions<VaultOptions>>(),
            sp.GetRequiredService<IOptions<CryptoOptions>>()));
        services.AddSingleton<ICryptoEngineFactory, CryptoEngineFactory>();
        services.AddScoped<IKekRotationService, KekRotationService>();

        // Resilience & active defence. Session revocation, the intrusion IP blacklist, and the
        // encrypted-envelope cache all hold shared runtime state. Back them with Redis when it is
        // configured — mandatory for multi-node scale-out so a revocation or block on one node is
        // honoured by every node — or with the in-process stores for a zero-infrastructure single node.
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        var redisOptions = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();
        if (redisOptions.Enabled)
        {
            // One multiplexer per process (the expensive, thread-safe singleton).
            services.AddSingleton<IConnectionMultiplexer>(RedisConnectionGuard.Connect(redisOptions));
            services.AddSingleton<ISecretCache, RedisSecretCache>();
            services.AddSingleton<IIpBlacklist, RedisIpBlacklist>();
            services.AddSingleton<ISessionRevocationService, RedisSessionRevocationService>();
            services.AddSingleton<ISessionRegistry, RedisSessionRegistry>();
            services.AddSingleton<IAuthThrottle, RedisAuthThrottle>();
            services.AddSingleton<IPasskeyCeremonyStore, RedisPasskeyCeremonyStore>();
        }
        else
        {
            services.AddSingleton<ISecretCache, MemorySecretCache>();
            services.AddSingleton<IIpBlacklist, InMemoryIpBlacklist>();
            services.AddSingleton<ISessionRevocationService, InMemorySessionRevocationService>();
            services.AddSingleton<ISessionRegistry, InMemorySessionRegistry>();
            services.AddSingleton<IAuthThrottle, InMemoryAuthThrottle>();
            services.AddSingleton<IPasskeyCeremonyStore, MemoryPasskeyCeremonyStore>();
        }
        services.AddScoped<IIntrusionResponseService, IntrusionResponseService>();

        // Dynamic secrets: one backend per DynamicSecretBackend value; the service picks by role.
        services.AddScoped<SqlServerBackend>();
        services.AddScoped<IDynamicSecretBackend>(sp => sp.GetRequiredService<SqlServerBackend>());
        services.AddScoped<IManagedSecretBackend>(sp => sp.GetRequiredService<SqlServerBackend>());
        services.AddScoped<IDynamicSecretService, DynamicSecretService>();

        // Runtime-managed trusted networks + audit reading.
        services.AddScoped<ITrustedNetworkService, TrustedNetworkService>();
        services.AddScoped<IAuditLogReader, AuditLogReader>();

        // Audit attestation: an ECDSA signer over the hash-chain head + the export service.
        services.AddSingleton<IAuditCheckpointSigner, EcdsaAuditCheckpointSigner>();
        services.AddScoped<IAuditCheckpointService, AuditCheckpointService>();

        // Licensing: resolve and verify the license once at startup (soft — it only informs the nudge
        // surfaces). A singleton so the token is read and verified exactly once per process.
        services.AddSingleton<ILicenseState, LicenseService>();

        // Soft recorder for on-demand premium-feature use (dynamic secrets, managed rotation,
        // attestation). Singleton so its per-feature dedup is process-wide.
        services.AddSingleton<IEvaluationWindowStore, EvaluationWindowStore>();
        services.AddSingleton<IPremiumFeatureUsage, PremiumFeatureUsageRecorder>();

        // Single source of truth for config-activated premium features (banner + startup check read it).
        services.AddSingleton<ConfiguredPremiumFeatures>();

        // Application services (pure Core classes, composed here).
        services.AddScoped<ISecretService, SecretService>();
        services.AddScoped<ISecretGrantService, SecretGrantService>();
        services.AddScoped<IAccessRequestService, AccessRequestService>();
        services.AddScoped<ServiceAccountService>();
        services.AddScoped<IServiceAccountService>(sp => sp.GetRequiredService<ServiceAccountService>());
        services.AddScoped<IApiKeyAuthenticator>(sp => sp.GetRequiredService<ServiceAccountService>());
        services.AddScoped<IVaultAuthenticationService, VaultAuthenticationService>();

        // SSO: the IdP proves who you are, this decides whether you may in. The policy is a Core
        // type bound from configuration here, so Core keeps no dependency on a config binder.
        var ssoSection = configuration.GetSection(SsoOptions.SectionName).Get<SsoOptions>();
        services.AddSingleton(ssoSection is null
            ? SsoPolicy.Default
            : new SsoPolicy(ssoSection.TrustIdpMultiFactor));
        services.AddScoped<ISsoSignInService, SsoSignInService>();

        // Read-only views of configuration for the presentation layer. They exist so a controller
        // can render "is SSO on" or "where does mail go" without taking a dependency on the options
        // types that answer it — those carry the OIDC client secret and the SMTP password.
        services.AddSingleton<ISsoAvailability, SsoAvailability>();
        services.AddSingleton<IEmailTransportStatus, EmailTransportStatus>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IMfaRecoveryService, MfaRecoveryService>();
        services.AddSingleton<CandidatePasswordHashing>();
        services.AddScoped<ProfileOperationPreparation>();
        services.AddScoped<AccountVerification>();
        services.AddScoped<VerifiedProfileChanges>();
        services.AddScoped<ProfileEmailChanges>();
        services.AddScoped<IEmailVerificationSendLimits, EmailVerificationSendLimits>();
        services.AddSingleton<SmtpEmailSender>();
        services.AddSingleton<IEmailVerificationDelivery, EmailVerificationDelivery>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<ISignInHistoryService, SignInHistoryService>();
        services.AddScoped<ISignedInDevices, SignedInDevices>();
        services.AddScoped<ISecurityCheckupService, SecurityCheckupService>();
        services.AddScoped<IPersonalDataExportService, PersonalDataExportService>();
        services.AddScoped<IPasskeyService, PasskeyService>();

        // Notifications: pick the email transport by config, mirror Email:Enabled into the
        // Core policy record, and compose/record through the notification service.
        services.AddSingleton(new NotificationOptions(
            configuration.GetValue<bool?>("Email:Enabled") ?? NotificationOptions.Default.Enabled));
        if (string.Equals(configuration.GetValue<string>("Email:Sender"), "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }
        services.AddScoped<INotificationService, NotificationService>();

        return services;
    }
}
