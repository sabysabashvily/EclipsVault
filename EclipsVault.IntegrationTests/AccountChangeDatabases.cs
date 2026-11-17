using System.Security.Cryptography;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Authentication;
using EclipsVault.Core.Application.Mfa;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Persistence.Interceptors;
using EclipsVault.Infrastructure.Persistence.Locking;
using EclipsVault.Infrastructure.Persistence.Repositories;
using EclipsVault.Infrastructure.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
/// One database at the current schema on a real server, saved through the vault's own audit
/// interceptor, for account changes that overlap. Each <see cref="AccountRequest"/> is what one
/// HTTP request has: its own context, and the repositories and services built on it.
/// </summary>
public abstract class AccountChangeDatabases : IAsyncLifetime
{
    private const string Database = "accounts";

    // The vault's real Argon2id, made cheap: these tests are about who wins, not hashing cost.
    private static readonly Argon2idPasswordHasher Hasher = new(Options.Create(
        new Argon2Options { Iterations = 1, MemoryKb = 1024, Parallelism = 1 }));

    // One chain per database, as a vault node holds one, so overlapping requests stamp in order.
    private AuditChain? _chain;

    protected abstract string Provider { get; }

    protected abstract IAuditChainLocker Locker { get; }

    protected abstract Task StartServerAsync();

    protected abstract Task StopServerAsync();

    protected abstract string ConnectionString(string database);

    public async Task InitializeAsync()
    {
        await StartServerAsync();
        _chain = new AuditChain(Locker);
        await using var request = NewRequest();
        await request.Context.GetService<IMigrator>().MigrateAsync();
    }

    public Task DisposeAsync() => StopServerAsync();

    public async Task RejectAuditWritesAsync()
    {
        await using var request = NewRequest();
        var sql = Provider == DatabaseProvider.Postgres
            ? "ALTER TABLE \"AuditLogs\" ADD CONSTRAINT \"CK_AccountTestsRejectAudits\" CHECK (1 = 0) NOT VALID"
            : "ALTER TABLE \"AuditLogs\" WITH NOCHECK ADD CONSTRAINT \"CK_AccountTestsRejectAudits\" CHECK (1 = 0)";
        await request.Context.Database.ExecuteSqlRawAsync(sql);
    }

    public async Task RestoreAuditWritesAsync()
    {
        await using var request = NewRequest();
        await request.Context.Database.ExecuteSqlRawAsync("ALTER TABLE \"AuditLogs\" DROP CONSTRAINT \"CK_AccountTestsRejectAudits\"");
    }

    public AccountRequest NewRequest(IInterceptor? fault = null, string database = Database)
    {
        var interceptor = new AuditSaveChangesInterceptor(
            new NoSession(), _chain!, TimeProvider.System, NullLogger<AuditSaveChangesInterceptor>.Instance);
        var options = new DbContextOptionsBuilder<EclipsVaultDbContext>();
        options.UseVaultDatabase(Provider, ConnectionString(database)).AddInterceptors(interceptor);
        if (fault is not null)
        {
            options.AddInterceptors(fault);
        }
        return new AccountRequest(new EclipsVaultDbContext(options.Options), interceptor, Hasher, Locker, _chain!);
    }

    /// <summary>A new account with an enrolled authenticator, saved directly as an administrator would create it.</summary>
    public async Task<User> CreateEnrolledAccountAsync(string username)
    {
        var account = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            DisplayName = username,
            Email = $"{username}@example.test",
            PasswordHash = new byte[32],
            PasswordSalt = new byte[16],
            TotpSecret = new TotpService(TimeProvider.System).GenerateSecret(),
            TotpEnabled = true,
            ProjectKey = "GLOBAL",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await using var request = NewRequest();
        await request.Users.AddAsync(account, default);
        return account;
    }

    private sealed class NoSession : IAuditContext
    {
        public Guid? UserId => null;
        public string? Username => null;
        public string? SourceIp => null;
    }
}

/// <summary>One request's context and the vault's own repositories and services built on it.</summary>
public sealed class AccountRequest : IAsyncDisposable
{
    public AccountRequest(EclipsVaultDbContext context, AuditSaveChangesInterceptor interceptor, IPasswordHasher hasher, IAuditChainLocker locks, AuditChain chain)
    {
        Context = context;
        Users = new UserRepository(context, TimeProvider.System);
        RecoveryCodes = new MfaRecoveryCodeRepository(context);
        var transaction = new AccountTransaction(context, locks);
        Changes = new AccountChangeCommitter(context, interceptor, transaction);
        ProfileOperations = new ProfileOperationRepository(context, transaction);
        EmailLimits = new EmailVerificationSendLimits(context, locks);
        Attempt = new AuthenticationAttempt(context, transaction, interceptor, chain);
        SecondFactors = new SecondFactorVerifier(Users, RecoveryCodes, new TotpService(TimeProvider.System), hasher, TimeProvider.System);
        Recovery = new MfaRecoveryService(RecoveryCodes, hasher, TimeProvider.System);
    }

    public EclipsVaultDbContext Context { get; }

    public UserRepository Users { get; }

    public MfaRecoveryCodeRepository RecoveryCodes { get; }

    public AccountChangeCommitter Changes { get; }

    public ProfileOperationRepository ProfileOperations { get; }

    public IEmailVerificationSendLimits EmailLimits { get; }

    public IAuthenticationAttempt Attempt { get; }

    public SecondFactorVerifier SecondFactors { get; }

    public MfaRecoveryService Recovery { get; }

    public async Task<User> LoadAsync(Guid accountId) => (await Users.FindByIdAsync(accountId, default))!;

    /// <summary>
    /// Seeds a new recovery-code set the way the verified Profile change commits one: staged, under
    /// <paramref name="credentialGeneration"/>, advancing it, in one commit with its audit row.
    /// </summary>
    public async Task<RecoveryCodeSet> ReplaceRecoveryCodesAsync(Guid accountId, long credentialGeneration)
    {
        var account = await LoadAsync(accountId);
        account.RequireCredentialGeneration(credentialGeneration);
        var codes = await Recovery.StageNewSetAsync(accountId, default);
        account.CredentialGeneration++;
        await Changes.CommitAsync(account, AuditAction.RecoveryCodesGenerated, "Recovery codes replaced", default);
        return new RecoveryCodeSet(codes, account.CredentialGeneration);
    }

    /// <summary>Seeds an authenticator reset the way the verified Profile change commits one.</summary>
    public async Task ResetAuthenticatorAsync(Guid accountId, long credentialGeneration)
    {
        var account = await LoadAsync(accountId);
        account.RequireCredentialGeneration(credentialGeneration);
        // Load the rows before changing the account, so a failed query leaves no reset staged.
        await RecoveryCodes.StageRemovalOfAllAsync(accountId, default);
        account.TotpSecret = null;
        account.TotpEnabled = false;
        account.LastTotpStep = null;
        account.CredentialGeneration++;
        await Changes.CommitAsync(account, AuditAction.SelfMfaReset, "Authenticator reset by owner", default);
    }

    public ValueTask DisposeAsync() => Context.DisposeAsync();
}

public sealed class SqlServerAccountChangeDatabases : AccountChangeDatabases
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder(VaultFixture.SqlServerImage)
        .WithName(ShareIssuerBackfill.ContainerName("account-sql"))
        .WithPassword("Sa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
        .Build();

    protected override string Provider => DatabaseProvider.SqlServer;

    protected override IAuditChainLocker Locker { get; } = new SqlServerAuditChainLocker();

    protected override Task StartServerAsync() => _sql.StartAsync();

    protected override Task StopServerAsync() => _sql.DisposeAsync().AsTask();

    protected override string ConnectionString(string database)
        => new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = database }.ConnectionString;
}

public sealed class PostgresAccountChangeDatabases : AccountChangeDatabases
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresShareIssuerBackfill.PostgresImage)
        .WithName(ShareIssuerBackfill.ContainerName("account-postgres"))
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
        .Build();

    protected override string Provider => DatabaseProvider.Postgres;

    protected override IAuditChainLocker Locker { get; } = new PostgresAuditChainLocker();

    protected override Task StartServerAsync() => _postgres.StartAsync();

    protected override Task StopServerAsync() => _postgres.DisposeAsync().AsTask();

    protected override string ConnectionString(string database)
        => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;
}

[CollectionDefinition(Name)]
public sealed class SqlServerAccountChangeCollection : ICollectionFixture<SqlServerAccountChangeDatabases>
{
    public const string Name = "account changes on SQL Server";
}

[CollectionDefinition(Name)]
public sealed class PostgresAccountChangeCollection : ICollectionFixture<PostgresAccountChangeDatabases>
{
    public const string Name = "account changes on PostgreSQL";
}
