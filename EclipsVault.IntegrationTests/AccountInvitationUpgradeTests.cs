using System.Security.Claims;
using System.Security.Cryptography;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Notifications;
using EclipsVault.Core.Application.Sessions;
using EclipsVault.Core.Application.Sso;
using EclipsVault.Core.Application.Users;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Notifications;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Persistence.Interceptors;
using EclipsVault.Infrastructure.Persistence.Locking;
using EclipsVault.Infrastructure.Persistence.Repositories;
using EclipsVault.Infrastructure.Security;
using EclipsVault.Web.Authentication;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// Dual-database server fixture proving the never-used-account data migration on both SQL Server
/// and PostgreSQL: selection rule, credential-generation advance, pending-secret clear, no audit write,
/// last-enabled-administrator preservation, pre-migration pending/SSO stamp refusal, and address replacement.
/// </summary>
public abstract class AccountInvitationUpgradeDatabases : IAsyncLifetime
{
    private const string MigrationBeforeInvitations = "ClearPendingDenialReasons";

    private readonly List<EclipsVaultDbContext> _contexts = [];

    protected abstract string Provider { get; }

    protected abstract Task StartServerAsync();

    protected abstract Task StopServerAsync();

    protected abstract string ConnectionStringFor(string database);

    public Task InitializeAsync() => StartServerAsync();

    public async Task DisposeAsync()
    {
        foreach (var context in _contexts)
        {
            await context.DisposeAsync();
        }

        await StopServerAsync();
    }

    public async Task<EclipsVaultDbContext> CreateBeforeInvitationsAsync(string database)
    {
        var context = Context(database);
        await context.GetService<IMigrator>().MigrateAsync(MigrationBeforeInvitations);
        return context;
    }

    public EclipsVaultDbContext FreshContext(string database) => Context(database);

    public IAuditChainLocker ChainLocker() => DatabaseProvider.IsPostgres(Provider)
        ? new PostgresAuditChainLocker()
        : new SqlServerAuditChainLocker();

    public static Task UpgradeAsync(EclipsVaultDbContext context) => context.GetService<IMigrator>().MigrateAsync();

    public static Task InsertPreMigrationUserAsync(
        EclipsVaultDbContext context,
        Guid id,
        string username,
        string email,
        ClearanceLevel clearance,
        bool isDisabled,
        bool totpEnabled,
        string? totpSecret,
        long credentialGeneration,
        DateTimeOffset createdAtUtc,
        byte[]? passwordHash = null,
        byte[]? passwordSalt = null)
        => context.Database.ExecuteSqlAsync($"""
            INSERT INTO "Users" ("Id", "Username", "DisplayName", "Email", "NormalizedEmail",
                "PasswordHash", "PasswordSalt", "TotpSecret", "TotpEnabled", "Clearance",
                "ProjectKey", "CredentialGeneration", "IsDisabled", "FailedAccessCount",
                "CreatedAtUtc", "ConcurrencyStamp")
            VALUES ({id}, {username}, {username}, {email}, {EclipsVault.Core.Domain.EmailIdentity.Normalize(email)},
                {passwordHash ?? new byte[32]}, {passwordSalt ?? new byte[16]}, {totpSecret}, {totpEnabled}, {(int)clearance},
                'GLOBAL', {credentialGeneration}, {isDisabled}, 0,
                {createdAtUtc}, {Guid.NewGuid()})
            """);

    public static Task InsertPasskeyAsync(EclipsVaultDbContext context, Guid userId)
        => context.Database.ExecuteSqlAsync($"""
            INSERT INTO "PasskeyCredentials" ("Id", "UserId", "CredentialId", "PublicKey", "SignCount", "Nickname", "CreatedAtUtc")
            VALUES ({Guid.NewGuid()}, {userId}, {RandomNumberGenerator.GetBytes(16)}, {RandomNumberGenerator.GetBytes(32)}, 1, 'Key', {DateTimeOffset.UtcNow})
            """);

    /// <summary>
    /// Records sign-in history in <paramref name="database"/> the way the vault does: through the audit sink, the
    /// group committer and the interceptor that chains each row. The audit tables are the same before and after
    /// the invitation upgrade, so the current model writes them.
    /// </summary>
    public async Task WriteAuditHistoryAsync(string database, params (AuditAction Action, Guid UserId, string Username)[] events)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAuditContext>(new HistoryActor());
        services.AddSingleton(ChainLocker());
        services.AddSingleton<AuditChain>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<EclipsVaultDbContext>((provider, options) => options
            .UseVaultDatabase(Provider, ConnectionStringFor(database))
            .AddInterceptors(provider.GetRequiredService<AuditSaveChangesInterceptor>()));
        services.AddSingleton<AuditGroupCommitter>();
        await using var provider = services.BuildServiceProvider();
        var committer = provider.GetRequiredService<AuditGroupCommitter>();
        await committer.StartAsync(default);
        try
        {
            var sink = new AuditSink(committer, provider.GetRequiredService<IAuditContext>(), TimeProvider.System, NullLogger<AuditSink>.Instance);
            foreach (var (action, userId, username) in events)
            {
                await sink.WriteAsync(new AuditEntry
                {
                    Action = action,
                    ResourceType = "User",
                    ResourceId = userId,
                    ActorUserId = userId,
                    ActorUsername = username,
                }, default);
            }
        }
        finally
        {
            await committer.StopAsync(default);
        }
    }

    private sealed class HistoryActor : IAuditContext
    {
        public Guid? UserId => null;
        public string? Username => null;
        public string? SourceIp => "127.0.0.1";
    }

    private EclipsVaultDbContext Context(string database)
    {
        var options = new DbContextOptionsBuilder<EclipsVaultDbContext>();
        options.UseVaultDatabase(Provider, ConnectionStringFor(database));
        var context = new EclipsVaultDbContext(options.Options);
        _contexts.Add(context);
        return context;
    }

    internal EclipsVaultDbContext AuditedContext(string database, AuditSaveChangesInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<EclipsVaultDbContext>();
        options.UseVaultDatabase(Provider, ConnectionStringFor(database)).AddInterceptors(interceptor);
        var context = new EclipsVaultDbContext(options.Options);
        _contexts.Add(context);
        return context;
    }
}

public abstract class AccountInvitationUpgradeTests(AccountInvitationUpgradeDatabases databases)
{
    private static readonly DateTimeOffset BaseTime = new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Selection_rule_generation_advance_pending_secret_clear_and_no_audit_write_hold_on_upgrade()
    {
        const string dbName = "never-used-selection";
        var pre = await databases.CreateBeforeInvitationsAsync(dbName);
        var activeAdminId = Guid.NewGuid();
        var neverUsedId = Guid.NewGuid();
        var passkeyUserId = Guid.NewGuid();
        var loginSucceededUserId = Guid.NewGuid();
        var passkeyLoginUserId = Guid.NewGuid();
        var failedOnlyUserId = Guid.NewGuid();
        var neverUsedAdminId = Guid.NewGuid();

        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, activeAdminId, "active-admin", "active.admin@eclipsvault.local",
            ClearanceLevel.TopSecret, false, totpEnabled: true, totpSecret: "ENROLLEDSECRET", credentialGeneration: 3, BaseTime);
        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, neverUsedId, "never-used", "never.used.1@eclipsvault.local",
            ClearanceLevel.Standard, false, totpEnabled: false, totpSecret: "COPIEDPENDINGSEED", credentialGeneration: 2, BaseTime.AddMinutes(1));
        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, passkeyUserId, "passkey-user", "passkey@eclipsvault.local",
            ClearanceLevel.Standard, false, totpEnabled: false, totpSecret: null, credentialGeneration: 1, BaseTime.AddMinutes(2));
        await AccountInvitationUpgradeDatabases.InsertPasskeyAsync(pre, passkeyUserId);
        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, loginSucceededUserId, "signed-in-reset", "signedin@eclipsvault.local",
            ClearanceLevel.Standard, false, totpEnabled: false, totpSecret: "RESETPENDINGSEED", credentialGeneration: 5, BaseTime.AddMinutes(3));
        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, passkeyLoginUserId, "passkey-login-removed", "passkeylogin@eclipsvault.local",
            ClearanceLevel.Standard, false, totpEnabled: false, totpSecret: null, credentialGeneration: 4, BaseTime.AddMinutes(4));
        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, failedOnlyUserId, "failed-only", "failed.only@eclipsvault.local",
            ClearanceLevel.Standard, false, totpEnabled: false, totpSecret: "FAILEDPENDINGSEED", credentialGeneration: 0, BaseTime.AddMinutes(5));
        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, neverUsedAdminId, "vault-admin", "vault-admin@eclipsvault.local",
            ClearanceLevel.TopSecret, false, totpEnabled: false, totpSecret: "ADMINPENDINGSEED", credentialGeneration: 1, BaseTime.AddMinutes(6));
        await databases.WriteAuditHistoryAsync(dbName,
            (AuditAction.LoginSucceeded, loginSucceededUserId, "signed-in-reset"),
            (AuditAction.PasskeyLogin, passkeyLoginUserId, "passkey-login-removed"),
            (AuditAction.LoginFailed, failedOnlyUserId, "failed-only"),
            (AuditAction.UserCreated, failedOnlyUserId, "failed-only"));

        await AccountInvitationUpgradeDatabases.UpgradeAsync(pre);

        var post = databases.FreshContext(dbName);
        var users = await post.Users.AsNoTracking().ToDictionaryAsync(u => u.Id);

        Assert.False(users[activeAdminId].AwaitingActivation);
        Assert.Equal(3, users[activeAdminId].CredentialGeneration);
        Assert.Equal("ENROLLEDSECRET", users[activeAdminId].TotpSecret);

        Assert.True(users[neverUsedId].AwaitingActivation);
        Assert.Equal(3, users[neverUsedId].CredentialGeneration);
        Assert.Null(users[neverUsedId].TotpSecret);

        Assert.False(users[passkeyUserId].AwaitingActivation);
        Assert.Equal(1, users[passkeyUserId].CredentialGeneration);

        Assert.False(users[loginSucceededUserId].AwaitingActivation);
        Assert.Equal(5, users[loginSucceededUserId].CredentialGeneration);
        Assert.Equal("RESETPENDINGSEED", users[loginSucceededUserId].TotpSecret);

        Assert.False(users[passkeyLoginUserId].AwaitingActivation);
        Assert.Equal(4, users[passkeyLoginUserId].CredentialGeneration);

        Assert.True(users[failedOnlyUserId].AwaitingActivation);
        Assert.Equal(1, users[failedOnlyUserId].CredentialGeneration);
        Assert.Null(users[failedOnlyUserId].TotpSecret);

        // Because activeAdminId is already active, even never-used vault-admin is invited.
        Assert.True(users[neverUsedAdminId].AwaitingActivation);
        Assert.Equal(2, users[neverUsedAdminId].CredentialGeneration);
        Assert.Null(users[neverUsedAdminId].TotpSecret);

        Assert.Equal(4, await post.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task A_sign_in_or_single_sign_on_begun_before_the_upgrade_gets_no_session_after_it()
    {
        const string dbName = "pre-upgrade-proofs";
        var pre = await databases.CreateBeforeInvitationsAsync(dbName);
        var neverUsedId = Guid.NewGuid();
        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, neverUsedId, "never-used", "never.used.1@eclipsvault.local",
            ClearanceLevel.Standard, false, totpEnabled: false, totpSecret: "COPIEDPENDINGSEED", credentialGeneration: 2, BaseTime);
        // What a node handed out before the upgrade, under the account's generation then: a pending sign-in
        // cookie, and a single sign-on decision about to become a session.
        var signedInBefore = UserDto.From(new User
        {
            Id = neverUsedId,
            Username = "never-used",
            DisplayName = "never-used",
            Email = "never.used.1@eclipsvault.local",
            ProjectKey = "GLOBAL",
            Clearance = ClearanceLevel.Standard,
            CredentialGeneration = 2,
        });
        var stampBefore = new SessionStamp(2, "rev-before");
        var pendingCookie = VaultClaimsFactory.CreatePendingMfaPrincipal(signedInBefore, stampBefore);

        await AccountInvitationUpgradeDatabases.UpgradeAsync(pre);

        var post = databases.FreshContext(dbName);
        Assert.False(await PendingCookieIsAcceptedAsync(post, pendingCookie));
        Assert.False(await IssueSessionAsync(post, signedInBefore, stampBefore, new SignInAudit(AuditAction.LoginSucceeded, "Single sign-on")));
        var auth = BuildAuthService(post, "rev-before");
        await Assert.ThrowsAsync<StaleProofException>(() => auth.BeginTotpEnrollmentAsync(neverUsedId, stampBefore, default));
        await Assert.ThrowsAsync<StaleProofException>(() => auth.CompleteTotpEnrollmentAsync(neverUsedId, stampBefore, "123456", default));
    }

    [Fact]
    public async Task A_proof_under_the_generation_the_upgrade_committed_still_gets_no_session_while_the_account_awaits_activation()
    {
        const string dbName = "post-upgrade-stamp";
        var pre = await databases.CreateBeforeInvitationsAsync(dbName);
        var neverUsedId = Guid.NewGuid();
        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, neverUsedId, "never-used", "never.used.1@eclipsvault.local",
            ClearanceLevel.Standard, false, totpEnabled: false, totpSecret: "COPIEDPENDINGSEED", credentialGeneration: 2, BaseTime);
        await AccountInvitationUpgradeDatabases.UpgradeAsync(pre);
        var post = databases.FreshContext(dbName);
        var migrated = await post.Users.AsNoTracking().SingleAsync(u => u.Id == neverUsedId);
        var stamp = new SessionStamp(migrated.CredentialGeneration, "rev-before");

        Assert.False(await PendingCookieIsAcceptedAsync(post, VaultClaimsFactory.CreatePendingMfaPrincipal(UserDto.From(migrated), stamp)));
        Assert.False(await IssueSessionAsync(post, UserDto.From(migrated), stamp, new SignInAudit(AuditAction.LoginSucceeded, "Single sign-on")));
        var auth = BuildAuthService(post, "rev-before");
        await Assert.ThrowsAsync<StaleProofException>(() => auth.BeginTotpEnrollmentAsync(neverUsedId, stamp, default));
        await Assert.ThrowsAsync<StaleProofException>(() => auth.VerifyTotpAsync(neverUsedId, stamp, "123456", default));
        var sso = new SsoSignInService(new UserRepository(post, TimeProvider.System), new RecordingAuditSink(), new FixedRevocation("rev-before"), new SsoPolicy(true));
        var decision = await sso.SignInAsync(new ExternalIdentity("https://idp.test", "sub-1", "never.used.1@eclipsvault.local", true, ["mfa"]), default);
        Assert.Equal(SsoOutcome.NotActivated, decision.Outcome);
    }

    [Fact]
    public async Task When_every_enabled_administrator_would_be_invited_vault_admin_stays_and_can_still_sign_in()
    {
        const string dbName = "preserve-vault-admin";
        var pre = await databases.CreateBeforeInvitationsAsync(dbName);
        var hasher = new Argon2idPasswordHasher(Options.Create(new Argon2Options { Iterations = 1, MemoryKb = 1024, Parallelism = 1 }));
        var existingPassword = "Pre-upgrade-admin-password-179!";
        var hashed = hasher.Hash(existingPassword);

        var earlierAdminId = Guid.NewGuid();
        var vaultAdminId = Guid.NewGuid();
        var standardId = Guid.NewGuid();

        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, earlierAdminId, "early-admin", "early.admin@eclipsvault.local",
            ClearanceLevel.TopSecret, false, false, "EARLYSEED", 0, BaseTime);
        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, vaultAdminId, "vault-admin", "vault-admin@eclipsvault.local",
            ClearanceLevel.TopSecret, false, false, null, 0, BaseTime.AddHours(1), hashed.Hash, hashed.Salt);
        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, standardId, "standard-one", "standard.one.1@eclipsvault.local",
            ClearanceLevel.Standard, false, false, "STDSEED", 0, BaseTime.AddHours(2));

        await AccountInvitationUpgradeDatabases.UpgradeAsync(pre);

        var post = databases.FreshContext(dbName);
        var users = await post.Users.AsNoTracking().ToDictionaryAsync(u => u.Id);

        Assert.False(users[vaultAdminId].AwaitingActivation);
        Assert.Equal(0, users[vaultAdminId].CredentialGeneration);

        Assert.True(users[earlierAdminId].AwaitingActivation);
        Assert.Equal(1, users[earlierAdminId].CredentialGeneration);
        Assert.Null(users[earlierAdminId].TotpSecret);

        Assert.True(users[standardId].AwaitingActivation);
        Assert.Equal(1, users[standardId].CredentialGeneration);
        Assert.Null(users[standardId].TotpSecret);

        // vault-admin still signs in with its existing password and reaches TOTP enrollment.
        var auth = BuildAuthService(post, "rev-1", hasher);
        var check = await auth.ValidateCredentialsAsync("vault-admin", existingPassword, default);
        Assert.Equal(CredentialStatus.RequiresTotpEnrollment, check.Status);
        Assert.Equal(vaultAdminId, check.User!.Id);
        var enrollment = await auth.BeginTotpEnrollmentAsync(vaultAdminId, check.Stamp!, default);
        var enrolled = await auth.CompleteTotpEnrollmentAsync(vaultAdminId, check.Stamp!,
            Totp.Code(enrollment.SecretBase32, Totp.CurrentStep()), default);
        Assert.NotNull(enrolled);
        var cookies = new CookieJar();
        Assert.True(await IssueSessionAsync(post, enrolled, check.Stamp!, new SignInAudit(AuditAction.LoginSucceeded, "Password + TOTP enrollment"), "rev-1", cookies));
        Assert.Equal(vaultAdminId, cookies.SignedIn?.GetUserId());
    }

    [Fact]
    public async Task A_migrated_accounts_replaced_address_is_the_one_resend_delivers_to()
    {
        const string dbName = "migrated-address";
        var pre = await databases.CreateBeforeInvitationsAsync(dbName);
        var vaultAdminId = Guid.NewGuid();
        var standardId = Guid.NewGuid();
        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, vaultAdminId, "vault-admin", "vault-admin@eclipsvault.local",
            ClearanceLevel.TopSecret, false, totpEnabled: true, totpSecret: "ENROLLEDSECRET", credentialGeneration: 0, BaseTime);
        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, standardId, "standard-one", "standard.one.1@eclipsvault.local",
            ClearanceLevel.Standard, false, totpEnabled: false, totpSecret: null, credentialGeneration: 0, BaseTime.AddHours(1));
        await AccountInvitationUpgradeDatabases.UpgradeAsync(pre);

        var chain = new AuditChain(databases.ChainLocker());
        var actor = new TestAdminActor(vaultAdminId);
        var interceptor = new AuditSaveChangesInterceptor(actor, chain, TimeProvider.System, NullLogger<AuditSaveChangesInterceptor>.Instance);
        var audited = databases.AuditedContext(dbName, interceptor);
        var delivery = new RecordingInvitationDelivery();
        var adminService = new UserAdminService(
            new UserRepository(audited, TimeProvider.System),
            new AccountInvitationRepository(audited),
            delivery,
            new FixedRevocation("rev-1"),
            new MfaRecoveryCodeRepository(audited),
            new RecordingAuditSink(),
            new AccountChangeCommitter(audited, interceptor, new AccountTransaction(audited, databases.ChainLocker())),
            actor,
            TimeProvider.System);

        var replaced = await adminService.ReplaceInvitationAddressAsync(standardId, "real.person@company.example", default);
        Assert.Equal("real.person@company.example", replaced);
        Assert.Empty(delivery.Sent);

        var sent = await adminService.ResendInvitationAsync(standardId, default);
        Assert.NotNull(sent);
        Assert.True(sent.Delivered);
        var mailed = Assert.Single(delivery.Sent);
        Assert.Equal("real.person@company.example", mailed.Address);
    }

    [Fact]
    public async Task When_vault_admin_is_disabled_the_earliest_created_enabled_administrator_stays()
    {
        const string dbName = "preserve-earliest-admin";
        var pre = await databases.CreateBeforeInvitationsAsync(dbName);
        var disabledVaultAdminId = Guid.NewGuid();
        var earliestEnabledAdminId = Guid.NewGuid();
        var laterEnabledAdminId = Guid.NewGuid();

        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, disabledVaultAdminId, "vault-admin", "vault-admin@eclipsvault.local",
            ClearanceLevel.TopSecret, isDisabled: true, false, null, 0, BaseTime);
        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, laterEnabledAdminId, "later-admin", "later@eclipsvault.local",
            ClearanceLevel.TopSecret, isDisabled: false, false, null, 0, BaseTime.AddHours(2));
        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, earliestEnabledAdminId, "earliest-admin", "earliest@eclipsvault.local",
            ClearanceLevel.TopSecret, isDisabled: false, false, null, 0, BaseTime.AddHours(1));

        await AccountInvitationUpgradeDatabases.UpgradeAsync(pre);

        var post = databases.FreshContext(dbName);
        var users = await post.Users.AsNoTracking().ToDictionaryAsync(u => u.Id);

        Assert.True(users[disabledVaultAdminId].AwaitingActivation);
        Assert.Equal(1, users[disabledVaultAdminId].CredentialGeneration);

        Assert.False(users[earliestEnabledAdminId].AwaitingActivation);
        Assert.Equal(0, users[earliestEnabledAdminId].CredentialGeneration);

        Assert.True(users[laterEnabledAdminId].AwaitingActivation);
        Assert.Equal(1, users[laterEnabledAdminId].CredentialGeneration);
    }

    private static VaultAuthenticationService BuildAuthService(
        EclipsVaultDbContext context,
        string revocationVersion,
        IPasswordHasher? hasher = null)
    {
        var users = new UserRepository(context, TimeProvider.System);
        var totp = new TotpService(TimeProvider.System);
        var recovery = new MfaRecoveryCodeRepository(context);
        var effectiveHasher = hasher ?? new Argon2idPasswordHasher(Options.Create(new Argon2Options { Iterations = 1, MemoryKb = 1024, Parallelism = 1 }));
        var secondFactors = new SecondFactorVerifier(users, recovery, totp, effectiveHasher, TimeProvider.System);
        var attempt = new DirectAttempt();
        var failures = new AuthenticationFailures(users, attempt, LockoutPolicy.Default, TimeProvider.System);
        return new VaultAuthenticationService(
            users,
            effectiveHasher,
            totp,
            recovery,
            secondFactors,
            failures,
            new RecordingAuditSink(),
            attempt,
            new FixedRevocation(revocationVersion),
            TimeProvider.System);
    }

    /// <summary>Runs <paramref name="cookie"/> through the pending sign-in cookie's check on the upgraded database.</summary>
    private static async Task<bool> PendingCookieIsAcceptedAsync(EclipsVaultDbContext post, ClaimsPrincipal cookie)
    {
        var context = new CookieValidatePrincipalContext(Request(post, "rev-before", new CookieJar()),
            new AuthenticationScheme(AuthSchemes.MfaPending, null, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(), new AuthenticationTicket(cookie, AuthSchemes.MfaPending));
        await SessionCookieValidation.ValidatePendingPrincipalAsync(context);
        return context.Principal is not null;
    }

    /// <summary>Asks the vault's session issuer, on the upgraded database, to turn a proof into a session.</summary>
    private static Task<bool> IssueSessionAsync(EclipsVaultDbContext post, UserDto user, SessionStamp stamp, SignInAudit audit,
        string revocationVersion = "rev-before", CookieJar? cookies = null)
    {
        var issuer = new SessionIssuer(new UserRepository(post, TimeProvider.System), new FixedRevocation(revocationVersion),
            new UnlistedSessions(), new DirectAttempt(), NullLogger<SessionIssuer>.Instance);
        return issuer.SignInAsync(Request(post, revocationVersion, cookies ?? new CookieJar()), user, stamp, audit, default);
    }

    private static DefaultHttpContext Request(EclipsVaultDbContext post, string revocationVersion, CookieJar cookies)
        => new()
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<IUserRepository>(new UserRepository(post, TimeProvider.System))
                .AddSingleton<ISessionRevocationService>(new FixedRevocation(revocationVersion))
                .AddSingleton<IAuthenticationService>(cookies)
                .BuildServiceProvider(),
        };

    /// <summary>Keeps the session cookie a sign-in would set, in place of a browser.</summary>
    private sealed class CookieJar : IAuthenticationService
    {
        public ClaimsPrincipal? SignedIn { get; private set; }

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            SignedIn = principal;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => throw new NotSupportedException();
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
    }

    /// <summary>A new session is listed after it is issued; nothing here reads the list.</summary>
    private sealed class UnlistedSessions : ISessionRegistry
    {
        public Task RecordSeenAsync(SessionObservation observation, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ActiveSession>> ListAsync(Guid userId, SessionStamp current, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RevokeAsync(Guid userId, Guid sessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TryRetireForReplacementAsync(Guid userId, Guid sessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> IsRevokedAsync(Guid userId, Guid sessionId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class DirectAttempt : IAuthenticationAttempt
    {
        public Task<T> RunAsync<T>(User account, Func<CancellationToken, Task<T>> step, CancellationToken ct) => step(ct);
        public void Record(AuditEntry entry) { }
    }

    private sealed class RecordingAuditSink : IAuditSink
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FixedRevocation(string version) : ISessionRevocationService
    {
        public Task RevokeAsync(Guid userId, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> FindAsync(Guid userId, CancellationToken ct) => Task.FromResult<string?>(version);
        public Task<string> CaptureAsync(Guid userId, CancellationToken ct) => Task.FromResult(version);
    }

    private sealed class TestAdminActor(Guid id) : IAuditContext
    {
        public Guid? UserId => id;
        public string? Username => "vault-admin";
        public string? SourceIp => "127.0.0.1";
    }

    private sealed class RecordingInvitationDelivery : IAccountInvitationDelivery
    {
        public bool Available => true;
        public List<(string Address, string Token)> Sent { get; } = [];

        public Task<bool> TrySendAsync(string address, string displayName, string username, string token, CancellationToken ct)
        {
            Sent.Add((address, token));
            return Task.FromResult(true);
        }
    }
}

public sealed class SqlServerAccountInvitationUpgradeDatabases : AccountInvitationUpgradeDatabases
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder(VaultFixture.SqlServerImage)
        .WithName(ShareIssuerBackfill.ContainerName("inv-sql"))
        .WithPassword("Sa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
        .Build();

    protected override string Provider => DatabaseProvider.SqlServer;

    protected override Task StartServerAsync() => _sql.StartAsync();

    protected override Task StopServerAsync() => _sql.DisposeAsync().AsTask();

    protected override string ConnectionStringFor(string database)
        => new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = database }.ConnectionString;
}

public sealed class PostgresAccountInvitationUpgradeDatabases : AccountInvitationUpgradeDatabases
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresShareIssuerBackfill.PostgresImage)
        .WithName(ShareIssuerBackfill.ContainerName("inv-pg"))
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
        .Build();

    protected override string Provider => DatabaseProvider.Postgres;

    protected override Task StartServerAsync() => _postgres.StartAsync();

    protected override Task StopServerAsync() => _postgres.DisposeAsync().AsTask();

    protected override string ConnectionStringFor(string database)
        => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;
}

[CollectionDefinition(Name)]
public sealed class SqlServerAccountInvitationUpgradeCollection : ICollectionFixture<SqlServerAccountInvitationUpgradeDatabases>
{
    public const string Name = "account invitation upgrade on SQL Server";
}

[CollectionDefinition(Name)]
public sealed class PostgresAccountInvitationUpgradeCollection : ICollectionFixture<PostgresAccountInvitationUpgradeDatabases>
{
    public const string Name = "account invitation upgrade on PostgreSQL";
}

[Collection(SqlServerAccountInvitationUpgradeCollection.Name)]
public sealed class SqlServerAccountInvitationUpgradeTests(SqlServerAccountInvitationUpgradeDatabases databases)
    : AccountInvitationUpgradeTests(databases);

[Collection(PostgresAccountInvitationUpgradeCollection.Name)]
public sealed class PostgresAccountInvitationUpgradeTests(PostgresAccountInvitationUpgradeDatabases databases)
    : AccountInvitationUpgradeTests(databases);
