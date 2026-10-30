using System.Security.Cryptography;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.AccessRequests;
using EclipsVault.Core.Application.Notifications;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Notifications;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Persistence.Interceptors;
using EclipsVault.Infrastructure.Persistence.Locking;
using EclipsVault.Infrastructure.Persistence.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// One database at the current schema on a real server, saved through the vault's own audit
/// interceptor, for shares issued at the same moment. Each <see cref="ShareRequest"/> is what one
/// HTTP request has: its own context, and the share service built on it.
/// </summary>
public abstract class ShareDatabases : IAsyncLifetime
{
    // One chain per database, as a vault node holds one, so overlapping requests stamp in order.
    private AuditChain? _chain;

    protected abstract string Provider { get; }

    protected abstract IAuditChainLocker Locker { get; }

    /// <summary>Counts the requests waiting for the audit chain lock, as a column named Value.</summary>
    protected abstract string QueuedChainWritersSql { get; }

    protected abstract Task StartServerAsync();

    protected abstract Task StopServerAsync();

    protected abstract string ConnectionString();

    public async Task InitializeAsync()
    {
        await StartServerAsync();
        _chain = new AuditChain(Locker);
        await using var context = NewContext(new Actor(null, null));
        await context.GetService<IMigrator>().MigrateAsync();
    }

    public Task DisposeAsync() => StopServerAsync();

    /// <summary>One request signed in as <paramref name="issuer"/>, its database calls passing through <paramref name="fault"/>.</summary>
    public ShareRequest NewRequest(User issuer, IInterceptor? fault = null)
    {
        var context = NewContext(new Actor(issuer.Id, issuer.Username), fault);
        var shares = new SecretGrantService(
            new SecretGrantRepository(context), new SecretRepository(context), new UserRepository(context, TimeProvider.System),
            new Actor(issuer.Id, issuer.Username), TimeProvider.System);
        var notices = new NotificationService(new LoggingEmailSender(NullLogger<LoggingEmailSender>.Instance),
            new EmailLogRepository(context), new UserRepository(context, TimeProvider.System), new NotificationOptions(false), TimeProvider.System);
        var requests = new AccessRequestService(new AccessRequestRepository(context), new SecretGrantRepository(context),
            new SecretRepository(context), new UserRepository(context, TimeProvider.System), new AccessRequestCommitter(context),
            new Actor(issuer.Id, issuer.Username), notices, TimeProvider.System);
        return new ShareRequest(context, shares, requests);
    }

    /// <summary>An Internal secret in one project and an issuer in it, with a recipient from another project.</summary>
    public async Task<(User Issuer, User Recipient, Secret Secret)> CreateSecretToShareAsync(string name)
    {
        var issuer = NewAccount($"{name}-issuer", "SHARES");
        var recipient = NewAccount($"{name}-recipient", "ELSEWHERE");
        var secret = new Secret
        {
            Id = Guid.NewGuid(),
            Name = name,
            ProjectKey = "SHARES",
            Ciphertext = [1],
            WrappedDek = [1],
            KekId = "test-kek",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = issuer.Id,
        };
        await using var context = NewContext(new Actor(issuer.Id, issuer.Username));
        context.AddRange(issuer, recipient, secret);
        await context.SaveChangesAsync();
        return (issuer, recipient, secret);
    }

    /// <summary>A share from <paramref name="issuer"/> to <paramref name="recipient"/> that expired an hour ago.</summary>
    public async Task<SecretGrant> ShareThatExpiredAsync(Secret secret, User issuer, User recipient)
    {
        var share = new SecretGrant
        {
            Id = Guid.NewGuid(),
            SecretId = secret.Id,
            GranteeUserId = recipient.Id,
            GranteeUsername = recipient.Username,
            IssuerUserId = issuer.Id,
            GrantedBy = issuer.Username,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
        };
        await using var context = NewContext(new Actor(issuer.Id, issuer.Username));
        context.SecretGrants.Add(share);
        await context.SaveChangesAsync();
        return share;
    }

    /// <summary>
    /// Saves a share straight to its table, as a request that saved first leaves it. It has no audit
    /// row: an audited save would wait for the audit chain, which the caller's request may hold.
    /// </summary>
    public async Task<Guid> SaveShareDirectlyAsync(Secret secret, User issuer, User recipient)
    {
        var shareId = Guid.NewGuid();
        await using var context = NewContext(new Actor(null, null));
        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO "SecretGrants" ("Id", "SecretId", "GranteeUserId", "GranteeUsername", "IssuerUserId", "GrantedBy", "CreatedAtUtc")
            VALUES ({shareId}, {secret.Id}, {recipient.Id}, {recipient.Username}, {issuer.Id}, {issuer.Username}, {DateTimeOffset.UtcNow})
            """);
        return shareId;
    }

    /// <summary>
    /// Holds the audit chain until disposed. Every share save takes the chain after its checks, so
    /// requests started meanwhile have all checked before any of them saves.
    /// </summary>
    public async Task<AuditChainHold> HoldAuditChainAsync()
    {
        var context = NewContext(new Actor(null, null));
        var transaction = await context.Database.BeginTransactionAsync();
        await _chain!.AcquireAsync(context, default);
        return new AuditChainHold(context, transaction, QueuedChainWritersSql);
    }

    private EclipsVaultDbContext NewContext(Actor actor, IInterceptor? fault = null)
    {
        var interceptor = new AuditSaveChangesInterceptor(
            actor, _chain!, TimeProvider.System, NullLogger<AuditSaveChangesInterceptor>.Instance);
        var options = new DbContextOptionsBuilder<EclipsVaultDbContext>();
        options.UseVaultDatabase(Provider, ConnectionString()).AddInterceptors(interceptor);
        if (fault is not null)
        {
            options.AddInterceptors(fault);
        }
        return new EclipsVaultDbContext(options.Options);
    }

    private static User NewAccount(string username, string project) => new()
    {
        Id = Guid.NewGuid(),
        Username = username,
        DisplayName = username,
        Email = $"{username}@example.test",
        PasswordHash = new byte[32],
        PasswordSalt = new byte[16],
        ProjectKey = project,
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };

    private sealed record Actor(Guid? UserId, string? Username) : IAuditContext
    {
        public string? SourceIp => "192.0.2.1";
    }
}

/// <summary>One request's context and the vault's own share service built on it.</summary>
public sealed class ShareRequest(EclipsVaultDbContext context, ISecretGrantService shares, IAccessRequestService requests) : IAsyncDisposable
{
    public EclipsVaultDbContext Context { get; } = context;

    public ISecretGrantService Shares { get; } = shares;

    public IAccessRequestService Requests { get; } = requests;

    public ValueTask DisposeAsync() => Context.DisposeAsync();
}

/// <summary>The audit chain lock, held by a transaction of its own until disposed.</summary>
public sealed class AuditChainHold(EclipsVaultDbContext context, IDbContextTransaction transaction, string queuedWritersSql) : IAsyncDisposable
{
    public async Task WaitForQueuedWritersAsync(int count)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await context.Database.SqlQueryRaw<int>(queuedWritersSql).SingleAsync(deadline.Token) < count)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await transaction.RollbackAsync();
        await transaction.DisposeAsync();
        await context.DisposeAsync();
    }
}

public sealed class SqlServerShareDatabases : ShareDatabases
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder(VaultFixture.SqlServerImage)
        .WithName(ShareIssuerBackfill.ContainerName("share-sql"))
        .WithPassword("Sa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
        .Build();

    protected override string Provider => DatabaseProvider.SqlServer;

    protected override IAuditChainLocker Locker { get; } = new SqlServerAuditChainLocker();

    protected override string QueuedChainWritersSql => """
        SELECT COUNT(*) AS [Value] FROM sys.dm_tran_locks
        WHERE resource_type = 'APPLICATION' AND resource_database_id = DB_ID()
            AND request_status = 'WAIT' AND resource_description LIKE '%EclipsVault:AuditChain%'
        """;

    protected override Task StartServerAsync() => _sql.StartAsync();

    protected override Task StopServerAsync() => _sql.DisposeAsync().AsTask();

    protected override string ConnectionString()
        => new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "shares" }.ConnectionString;
}

public sealed class PostgresShareDatabases : ShareDatabases
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresShareIssuerBackfill.PostgresImage)
        .WithName(ShareIssuerBackfill.ContainerName("share-postgres"))
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
        .Build();

    protected override string Provider => DatabaseProvider.Postgres;

    protected override IAuditChainLocker Locker { get; } = new PostgresAuditChainLocker();

    // Only share saves run on this database, so every advisory-lock waiter is queued for the audit chain.
    protected override string QueuedChainWritersSql => """
        SELECT COUNT(*)::int AS "Value" FROM pg_locks
        WHERE locktype = 'advisory' AND NOT granted
            AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
        """;

    protected override Task StartServerAsync() => _postgres.StartAsync();

    protected override Task StopServerAsync() => _postgres.DisposeAsync().AsTask();

    protected override string ConnectionString()
        => new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = "shares" }.ConnectionString;
}

[CollectionDefinition(Name)]
public sealed class SqlServerShareCollection : ICollectionFixture<SqlServerShareDatabases>
{
    public const string Name = "shares on SQL Server";
}

[CollectionDefinition(Name)]
public sealed class PostgresShareCollection : ICollectionFixture<PostgresShareDatabases>
{
    public const string Name = "shares on PostgreSQL";
}
