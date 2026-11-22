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
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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

    public static Task InsertAuditRowAsync(
        EclipsVaultDbContext context,
        long sequence,
        AuditAction action,
        Guid userId,
        string username)
        => context.Database.ExecuteSqlAsync($"""
            INSERT INTO "AuditLogs" ("Id", "TimestampUtc", "UserId", "Username", "SourceIp",
                "Action", "ResourceType", "ResourceId", "Details", "IsCritical",
                "Sequence", "PreviousHash", "EntryHash", "HashVersion")
            VALUES ({Guid.NewGuid()}, {DateTimeOffset.UtcNow}, {userId}, {username}, '127.0.0.1',
                {(int)action}, 'User', {userId}, 'pre-migration row', {false},
                {sequence}, {new string('0', 64)}, {Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}, 2)
            """);

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
        await AccountInvitationUpgradeDatabases.InsertAuditRowAsync(pre, 1, AuditAction.LoginSucceeded, loginSucceededUserId, "signed-in-reset");
        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, passkeyLoginUserId, "passkey-login-removed", "passkeylogin@eclipsvault.local",
            ClearanceLevel.Standard, false, totpEnabled: false, totpSecret: null, credentialGeneration: 4, BaseTime.AddMinutes(4));
        await AccountInvitationUpgradeDatabases.InsertAuditRowAsync(pre, 2, AuditAction.PasskeyLogin, passkeyLoginUserId, "passkey-login-removed");
        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, failedOnlyUserId, "failed-only", "failed.only@eclipsvault.local",
            ClearanceLevel.Standard, false, totpEnabled: false, totpSecret: "FAILEDPENDINGSEED", credentialGeneration: 0, BaseTime.AddMinutes(5));
        await AccountInvitationUpgradeDatabases.InsertAuditRowAsync(pre, 3, AuditAction.LoginFailed, failedOnlyUserId, "failed-only");
        await AccountInvitationUpgradeDatabases.InsertAuditRowAsync(pre, 4, AuditAction.UserCreated, failedOnlyUserId, "failed-only");
        await AccountInvitationUpgradeDatabases.InsertPreMigrationUserAsync(pre, neverUsedAdminId, "vault-admin", "vault-admin@eclipsvault.local",
            ClearanceLevel.TopSecret, false, totpEnabled: false, totpSecret: "ADMINPENDINGSEED", credentialGeneration: 1, BaseTime.AddMinutes(6));

        var preMigrationStamp = new SessionStamp(2, "rev-before");
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

        // The migration writes no audit rows.
        Assert.Equal(4, await post.AuditLogs.CountAsync());

        // Pre-migration pending stamp and SSO assertion are refused after migration.
        var auth = BuildAuthService(post, "rev-before");
        await Assert.ThrowsAsync<StaleProofException>(() => auth.BeginTotpEnrollmentAsync(neverUsedId, preMigrationStamp, default));
        await Assert.ThrowsAsync<StaleProofException>(() => auth.CompleteTotpEnrollmentAsync(neverUsedId, preMigrationStamp, "123456", default));
        // Even a stamp with the post-migration generation (in-flight SSO / old node) is refused because AwaitingActivation is true.
        var postMigrationStamp = new SessionStamp(users[neverUsedId].CredentialGeneration, "rev-before");
        await Assert.ThrowsAsync<StaleProofException>(() => auth.BeginTotpEnrollmentAsync(neverUsedId, postMigrationStamp, default));
        await Assert.ThrowsAsync<StaleProofException>(() => auth.VerifyTotpAsync(neverUsedId, postMigrationStamp, "123456", default));

        var ssoAudit = new RecordingAuditSink();
        var sso = new SsoSignInService(new UserRepository(post, TimeProvider.System), ssoAudit, new FixedRevocation("rev-before"), new SsoPolicy(true));
        var ssoDecision = await sso.SignInAsync(new ExternalIdentity("https://idp.test", "sub-1", "never.used.1@eclipsvault.local", true, ["mfa"]), default);
        Assert.Equal(SsoOutcome.NotActivated, ssoDecision.Outcome);
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

        // Replacing the generated address of a migrated account makes resend deliver to the replaced address.
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
    public async Task When_vault_admin_is_disabled_or_absent_the_earliest_created_enabled_administrator_stays()
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
