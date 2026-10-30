using System.Security.Cryptography;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// A fresh database holding one access request in each status, each carrying denial text its requester
/// wrote, migrated once through the migration that clears that text from pending requests.
/// </summary>
public abstract class PendingDenialReasonsCleanup : IAsyncLifetime
{
    private const string MigrationBeforeCleanup = "AddMailboxVerification";

    public const string RequesterWrittenText = "Pre-approved by security";

    private EclipsVaultDbContext? _db;

    protected abstract string Provider { get; }

    /// <summary>Starts the database server and returns a connection string to a database the migrations may create.</summary>
    protected abstract Task<string> StartDatabaseAsync();

    protected abstract Task StopDatabaseAsync();

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<EclipsVaultDbContext>();
        options.UseVaultDatabase(Provider, await StartDatabaseAsync());
        _db = new EclipsVaultDbContext(options.Options);
        var migrator = _db.GetService<IMigrator>();

        await migrator.MigrateAsync(MigrationBeforeCleanup);
        await InsertRequestsAsync(_db);
        await migrator.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        await StopDatabaseAsync();
    }

    public Task<string?> DeniedReasonsOfAsync(AccessRequestStatus status)
        => _db!.AccessRequests.AsNoTracking().Where(r => r.Status == status).Select(r => r.DeniedReasons).SingleAsync();

    private static async Task InsertRequestsAsync(EclipsVaultDbContext db)
    {
        var requester = new User
        {
            Id = Guid.NewGuid(),
            Username = "requester",
            DisplayName = "requester",
            Email = "requester@example.test",
            PasswordHash = new byte[32],
            PasswordSalt = new byte[16],
            ProjectKey = "ELSEWHERE",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        var secret = new Secret
        {
            Id = Guid.NewGuid(),
            Name = "deployment-key",
            ProjectKey = "GLOBAL",
            Ciphertext = [1],
            WrappedDek = [1],
            KekId = "test-kek",
            Algorithm = "AES-256-GCM",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.AddRange(requester, secret,
            RequestIn(AccessRequestStatus.Pending), RequestIn(AccessRequestStatus.Approved),
            RequestIn(AccessRequestStatus.Rejected), RequestIn(AccessRequestStatus.Cancelled));

        AccessRequest RequestIn(AccessRequestStatus status) => new()
        {
            Id = Guid.NewGuid(),
            SecretId = secret.Id,
            SecretName = secret.Name,
            ProjectKey = secret.ProjectKey,
            RequesterUserId = requester.Id,
            RequesterUsername = requester.Username,
            Reason = "Need this for deployment",
            DeniedReasons = RequesterWrittenText,
            Status = status,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await db.SaveChangesAsync();
    }
}

public sealed class SqlServerPendingDenialReasonsCleanup : PendingDenialReasonsCleanup
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder(VaultFixture.SqlServerImage)
        .WithName(ShareIssuerBackfill.ContainerName("sql"))
        .WithPassword("Sa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
        .Build();

    protected override string Provider => DatabaseProvider.SqlServer;

    protected override async Task<string> StartDatabaseAsync()
    {
        await _sql.StartAsync();
        return new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "EclipsVault" }.ConnectionString;
    }

    protected override Task StopDatabaseAsync() => _sql.DisposeAsync().AsTask();
}

public sealed class PostgresPendingDenialReasonsCleanup : PendingDenialReasonsCleanup
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresShareIssuerBackfill.PostgresImage)
        .WithName(ShareIssuerBackfill.ContainerName("postgres"))
        .WithDatabase("eclipsvault")
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
        .Build();

    protected override string Provider => DatabaseProvider.Postgres;

    protected override async Task<string> StartDatabaseAsync()
    {
        await _postgres.StartAsync();
        return _postgres.GetConnectionString();
    }

    protected override Task StopDatabaseAsync() => _postgres.DisposeAsync().AsTask();
}
