using System.Text;
using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Infrastructure.Persistence;
using EclipsVault.Infrastructure.Security;
using EclipsVault.Tests.Fakes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EclipsVault.Tests.Persistence;

/// <summary>
/// A vault must never invent its own administrator.
///
/// The seeder used to create 'vault-admin' with a password committed to this repository whenever it
/// found an empty database, in every environment, because startup always called it. Anyone who had
/// read the source held TopSecret clearance over every project on any deployment whose operator had
/// not thought to override it — and nothing about a healthy startup would have said so.
///
/// These run the real seeder against in-memory SQLite. The demo world is pinned as tightly as the
/// refusals: the value of "Development only" is entirely in what does *not* appear elsewhere.
/// </summary>
public class DbSeederTests : IDisposable
{
    private const string GoodPassword = "b8Kq2-Vx7r_Ln4Wd9Ts6";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<EclipsVaultDbContext> _options;

    public DbSeederTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<EclipsVaultDbContext>().UseSqlite(_connection).Options;

        using var db = new EclipsVaultDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "EclipsVault.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>
    /// Records the password it was given instead of running Argon2, which is deliberately slow.
    /// What is under test is which password reaches the hasher, not how it is hashed.
    /// </summary>
    private sealed class RecordingHasher : IPasswordHasher
    {
        public PasswordHashResult Hash(string password) => new(Encoding.UTF8.GetBytes(password), []);

        public bool Verify(string password, byte[] hash, byte[] salt)
            => Encoding.UTF8.GetString(hash) == password;
    }

    private IServiceProvider Services(string? adminPassword)
    {
        var settings = new Dictionary<string, string?>();
        if (adminPassword is not null)
        {
            settings["Seed:AdminPassword"] = adminPassword;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddSingleton<IPasswordHasher, RecordingHasher>();

        // The real corpus, so the published development password is screened for real.
        services.AddSingleton<IBreachedPasswordScreen, BundledBreachedPasswordScreen>();
        services.AddSingleton<ICryptoEngineFactory>(new FakeCryptoEngine());
        services.AddScoped(_ => new EclipsVaultDbContext(_options));
        return services.BuildServiceProvider();
    }

    private Task SeedAsync(string environment, string? adminPassword = null) =>
        DbSeeder.SeedAsync(Services(adminPassword), new StubEnvironment { EnvironmentName = environment });

    private EclipsVaultDbContext Read() => new(_options);

    // ---- production refuses rather than invent a credential -------------------------------

    [Fact]
    public async Task Production_refuses_to_start_an_empty_vault_with_no_bootstrap_password()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => SeedAsync(Environments.Production));

        Assert.Contains("Seed:AdminPassword", ex.Message, StringComparison.Ordinal);
        await using var db = Read();
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task Production_refuses_the_development_password_published_in_this_repository()
    {
        // The realistic mistake: copying the development configuration to a real deployment.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SeedAsync(Environments.Production, "ChangeMe!Umbra#2026-Admin"));

        Assert.Contains("compromised", ex.Message, StringComparison.OrdinalIgnoreCase);
        await using var db = Read();
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task Production_refuses_a_password_below_the_minimum_everyone_else_is_held_to()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SeedAsync(Environments.Production, "short1!"));

        await using var db = Read();
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task An_unset_environment_is_treated_as_production_and_fails_closed()
    {
        // ASPNETCORE_ENVIRONMENT unset must not be a way back to seeded credentials.
        await Assert.ThrowsAsync<InvalidOperationException>(() => SeedAsync(""));

        await using var db = Read();
        Assert.Empty(await db.Users.ToListAsync());
    }

    // ---- production bootstraps exactly one account and no sample data ---------------------

    [Fact]
    public async Task Production_bootstraps_one_administrator_from_the_supplied_password()
    {
        await SeedAsync(Environments.Production, GoodPassword);

        await using var db = Read();
        var user = Assert.Single(await db.Users.ToListAsync());
        Assert.Equal("vault-admin", user.Username);
        Assert.Equal(ClearanceLevel.TopSecret, user.Clearance);
        Assert.True(new RecordingHasher().Verify(GoodPassword, user.PasswordHash, user.PasswordSalt));
    }

    [Fact]
    public async Task Production_seeds_no_dev_account_no_sample_secrets_and_no_decoys()
    {
        await SeedAsync(Environments.Production, GoodPassword);

        await using var db = Read();
        Assert.DoesNotContain(await db.Users.ToListAsync(), u => u.Username == "dev-user");

        // Sample secrets carry values committed to this repository, and the decoys are bait chosen
        // for a demo. Neither belongs in a real vault.
        Assert.Empty(await db.Secrets.ToListAsync());
        Assert.Empty(await db.DynamicSecretRoles.ToListAsync());
    }

    [Fact]
    public async Task Bootstrap_does_not_run_again_once_an_account_exists()
    {
        await SeedAsync(Environments.Production, GoodPassword);

        // Second start with the setting removed, as the log tells the operator to do.
        await SeedAsync(Environments.Production);

        await using var db = Read();
        Assert.Single(await db.Users.ToListAsync());
    }

    // ---- development still gets its demo world -------------------------------------------

    [Fact]
    public async Task Development_seeds_both_staff_accounts_without_any_configuration()
    {
        await SeedAsync(Environments.Development);

        await using var db = Read();
        var users = await db.Users.ToListAsync();
        Assert.Equal(2, users.Count);
        Assert.Contains(users, u => u.Username == "vault-admin");
        Assert.Contains(users, u => u.Username == "dev-user");

        var dev = users.Single(u => u.Username == "dev-user");
        Assert.True(new RecordingHasher().Verify("ChangeMe!Umbra#2026-Dev", dev.PasswordHash, dev.PasswordSalt));
    }

    [Fact]
    public async Task Development_seeds_the_sample_secrets_and_the_decoys()
    {
        await SeedAsync(Environments.Development);

        await using var db = Read();
        var secrets = await db.Secrets.ToListAsync();
        Assert.NotEmpty(secrets);
        Assert.Equal(2, secrets.Count(s => s.IsHoneyToken));
        Assert.NotEmpty(await db.DynamicSecretRoles.ToListAsync());
    }

    [Fact]
    public async Task Development_adds_the_postgres_roles_to_a_database_seeded_with_only_the_sql_server_ones()
    {
        await SeedAsync(Environments.Development);
        await using (var seeded = Read())
        {
            await seeded.DynamicSecretRoles.Where(role => role.Backend == DynamicSecretBackend.Postgres).ExecuteDeleteAsync();
        }

        await SeedAsync(Environments.Development);

        await using var db = Read();
        var roles = await db.DynamicSecretRoles.OrderBy(role => role.Name).Select(role => new { role.Name, role.Backend }).ToListAsync();
        Assert.Equal(
            new[]
            {
                new { Name = "global_db_writer", Backend = DynamicSecretBackend.SqlServer },
                new { Name = "global_pg_writer", Backend = DynamicSecretBackend.Postgres },
                new { Name = "phoenix_db_reader", Backend = DynamicSecretBackend.SqlServer },
                new { Name = "phoenix_pg_reader", Backend = DynamicSecretBackend.Postgres },
            },
            roles);
    }

    [Fact]
    public async Task Development_leaves_a_deleted_sql_server_sample_deleted()
    {
        await SeedAsync(Environments.Development);
        await using (var seeded = Read())
        {
            await seeded.DynamicSecretRoles.Where(role => role.Name == "phoenix_db_reader").ExecuteDeleteAsync();
        }

        await SeedAsync(Environments.Development);

        await using var db = Read();
        Assert.Equal(["global_db_writer", "global_pg_writer", "phoenix_pg_reader"],
            await db.DynamicSecretRoles.OrderBy(role => role.Name).Select(role => role.Name).ToListAsync());
    }

    [Fact]
    public async Task Development_adds_no_postgres_sample_whose_name_a_case_insensitive_database_already_holds()
    {
        await CompareRoleNamesWithoutCaseAsync();
        await using (var operatorEdit = Read())
        {
            operatorEdit.DynamicSecretRoles.Add(new DynamicSecretRole
            {
                Id = Guid.NewGuid(),
                Name = "PHOENIX_PG_READER",
                Description = "Renamed by a developer.",
                ProjectKey = "PHOENIX",
                Backend = DynamicSecretBackend.Postgres,
                CreationStatements = "-",
                RevocationStatements = "-",
                CreatedAtUtc = DateTimeOffset.UnixEpoch,
            });
            await operatorEdit.SaveChangesAsync();
        }

        await SeedAsync(Environments.Development);

        await using var db = Read();
        Assert.Equal(["global_pg_writer", "PHOENIX_PG_READER"],
            await db.DynamicSecretRoles.OrderBy(role => role.Name).Select(role => role.Name).ToListAsync());
    }

    /// <summary>
    /// Rebuilds the empty roles table so its names compare without case, as SQL Server's default collation
    /// does; SQLite's own default compares them exactly.
    /// </summary>
    private async Task CompareRoleNamesWithoutCaseAsync()
    {
        await using var db = Read();
        var connection = db.Database.GetDbConnection();
        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'DynamicSecretRoles'";
        var create = (string)(await read.ExecuteScalarAsync())!;
        await db.Database.ExecuteSqlRawAsync("DROP TABLE \"DynamicSecretRoles\"");
        await db.Database.ExecuteSqlRawAsync(create.Replace("\"Name\" TEXT NOT NULL", "\"Name\" TEXT NOT NULL COLLATE NOCASE", StringComparison.Ordinal));
        await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX \"IX_DynamicSecretRoles_Name\" ON \"DynamicSecretRoles\" (\"Name\")");
    }
}

