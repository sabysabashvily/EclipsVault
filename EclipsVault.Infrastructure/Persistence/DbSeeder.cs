using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EclipsVault.Infrastructure.Persistence;

/// <summary>
/// Seeds data. The schema is <see cref="DatabaseMigrator"/>'s job; this does one of two very
/// different jobs depending on where it is running.
///
/// In Development it seeds the demo world: two staff accounts with the passwords published in this
/// repository, sample project secrets, one short-TTL secret to demonstrate the lifecycle shredder,
/// the honey-token decoys, and the dynamic-secret roles.
///
/// Anywhere else it seeds none of that. Sample data carries values committed to a public
/// repository, so every one of them is a credential an attacker already has; a vault that invents
/// its own administrator on first boot is a vault anyone who has read this source can sign in to.
/// Production gets the schema and, if the operator supplies a password out-of-band, one
/// administrator account — and nothing at all if they do not.
/// </summary>
public static class DbSeeder
{
    /// <param name="environment">
    /// Decides whether the demo world is seeded. Anything other than Development is treated as
    /// production: an unset ASPNETCORE_ENVIRONMENT must fail closed, not seed known credentials.
    /// </param>
    public static async Task SeedAsync(IServiceProvider services, IHostEnvironment environment)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<EclipsVaultDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("EclipsVault.DbSeeder");

        var clock = sp.GetRequiredService<TimeProvider>();
        var now = clock.GetUtcNow();

        if (!environment.IsDevelopment())
        {
            await BootstrapAdministratorAsync(sp, db, logger, now);
            return;
        }

        await SeedDevelopmentDataAsync(sp, db, logger, now);
    }

    /// <summary>
    /// Creates the first administrator from a password the operator supplies out-of-band, once,
    /// on an empty vault. There is deliberately no fallback value: a default password in source is
    /// a published password, and this account holds TopSecret clearance over every project.
    ///
    /// Refusing to start is the correct outcome when the password is missing or weak. The
    /// alternative — booting with an account whose credentials are in this repository — is a vault
    /// that is already breached at the moment it becomes reachable.
    /// </summary>
    private static async Task BootstrapAdministratorAsync(
        IServiceProvider sp, EclipsVaultDbContext db, ILogger logger, DateTimeOffset now)
    {
        if (await db.Users.AnyAsync())
        {
            return; // Already bootstrapped. Accounts are managed in the app from here on.
        }

        var configuration = sp.GetRequiredService<IConfiguration>();
        var password = configuration["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "This vault has no accounts and no bootstrap password, so it will not start. Set " +
                "Seed:AdminPassword (as the Seed__AdminPassword environment variable, or an entry " +
                "in your secret manager) to a password you choose, start once to create " +
                "'vault-admin', then remove the setting — it is only read on an empty vault. " +
                "Enrol TOTP on the first sign-in.");
        }

        if (password.Length < MinimumBootstrapPasswordLength)
        {
            throw new InvalidOperationException(
                $"Seed:AdminPassword must be at least {MinimumBootstrapPasswordLength} characters — " +
                "the same minimum this vault enforces on everyone else's password.");
        }

        // The same screen every user-set password passes through. The seeder used to skip it
        // entirely, which is how a published password could become the administrator's.
        if (sp.GetRequiredService<IBreachedPasswordScreen>().IsCompromised(password))
        {
            throw new InvalidOperationException(
                "Seed:AdminPassword appears in this vault's compromised-password corpus, so it is " +
                "already known to attackers. If it came from this repository's development " +
                "configuration, that is exactly the mistake this check exists to catch. Choose a " +
                "password unique to this deployment.");
        }

        var hash = sp.GetRequiredService<IPasswordHasher>().Hash(password);

        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = "vault-admin",
            DisplayName = "Vault Administrator",
            Email = "vault-admin@eclipsvault.local",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            Clearance = ClearanceLevel.TopSecret,
            ProjectKey = "GLOBAL",
            CreatedAtUtc = now
        });

        await db.SaveChangesAsync();
        logger.LogWarning(
            "Bootstrapped administrator {AdminUser} from Seed:AdminPassword on an empty vault. Sign in, " +
            "enrol TOTP, then remove the setting — it is not read again once an account exists.",
            "vault-admin");
    }

    private const int MinimumBootstrapPasswordLength = 12;

    /// <summary>
    /// The demo world: known-credential accounts and sample secrets whose values are committed to
    /// this repository. Development only, and unreachable from anywhere else — see
    /// <see cref="SeedAsync"/>.
    /// </summary>
    private static async Task SeedDevelopmentDataAsync(
        IServiceProvider sp, EclipsVaultDbContext db, ILogger logger, DateTimeOffset now)
    {
        if (!await db.Users.AnyAsync())
        {
            var hasher = sp.GetRequiredService<IPasswordHasher>();
            var configuration = sp.GetRequiredService<IConfiguration>();

            var adminPassword = configuration["Seed:AdminPassword"] ?? "ChangeMe!Umbra#2026-Admin";
            var devPassword = configuration["Seed:DevPassword"] ?? "ChangeMe!Umbra#2026-Dev";

            var adminHash = hasher.Hash(adminPassword);
            var devHash = hasher.Hash(devPassword);

            db.Users.AddRange(
                new User
                {
                    Id = Guid.NewGuid(),
                    Username = "vault-admin",
                    DisplayName = "Vault Administrator",
                    Email = "vault-admin@eclipsvault.local",
                    PasswordHash = adminHash.Hash,
                    PasswordSalt = adminHash.Salt,
                    Clearance = ClearanceLevel.TopSecret,
                    ProjectKey = "GLOBAL",
                    CreatedAtUtc = now
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    Username = "dev-user",
                    DisplayName = "Dev User",
                    Email = "dev-user@eclipsvault.local",
                    PasswordHash = devHash.Hash,
                    PasswordSalt = devHash.Salt,
                    Clearance = ClearanceLevel.Standard,
                    ProjectKey = "PHOENIX",
                    CreatedAtUtc = now
                });

            await db.SaveChangesAsync();
            logger.LogInformation("Seeded staff accounts {AdminUser} and {DevUser} (TOTP enrollment happens on first sign-in)",
                "vault-admin", "dev-user");
        }

        if (!await db.Secrets.AnyAsync())
        {
            var engine = sp.GetRequiredService<ICryptoEngineFactory>().Create();

            // Every credential string below is synthetic — invented for this dev-only seed, never a
            // real secret — and the two GLOBAL/Production entries are honey-token decoys by design. A
            // secret scanner will flag them; they are safe to ignore.
            db.Secrets.AddRange(
                await SealSecretAsync(engine, now, "Phoenix_Dev_Database_Password", "PHOENIX", SecretEnvironment.Development,
                    SensitivityLevel.Internal, "Server=phx-dev-sql;Database=Phoenix;User Id=phx_app;Password=dev-only-Sample!42"),
                await SealSecretAsync(engine, now, "Phoenix_Staging_Api_Key", "PHOENIX", SecretEnvironment.Staging,
                    SensitivityLevel.Confidential, "phx_stg_api_9f83b2e1c4d5460fb7a1"),
                await SealSecretAsync(engine, now, "Phoenix_Ephemeral_Deploy_Token", "PHOENIX", SecretEnvironment.Development,
                    SensitivityLevel.Internal, "deploy-token-expires-soon-5f6e7d8c",
                    expiresAtUtc: now.AddMinutes(5)),

                // Honey-token decoys: realistic bait. Any by-id read trips the intrusion response.
                await SealSecretAsync(engine, now, "Production_AWS_Root_Key", "GLOBAL", SecretEnvironment.Production,
                    SensitivityLevel.TopSecret, "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
                    isHoneyToken: true),
                await SealSecretAsync(engine, now, "Global_SQL_SA_Password", "GLOBAL", SecretEnvironment.Production,
                    SensitivityLevel.TopSecret, "Sup3r$ecretS4-Pr0d-2026!",
                    isHoneyToken: true));

            await db.SaveChangesAsync();
            logger.LogInformation("Seeded sample secrets, one short-TTL secret, and {HoneyTokenCount} honey-token decoys", 2);
        }

        await SeedDynamicSecretRolesAsync(db, logger, now);
    }

    // The PostgreSQL samples' statements, public so the integration tests run these rather than copies.
    // PostgreSQL folds an unquoted name to lower case, and the minted name has capitals, so it is
    // always quoted. VALID UNTIL stops the password working at the lease's end even if revocation fails;
    // {{expiration}} is UTC, which the '+00' says.
    public const string PostgresReaderRecipe = """
        CREATE ROLE "{{name}}" LOGIN PASSWORD '{{password}}' VALID UNTIL '{{expiration}}+00' IN ROLE pg_read_all_data;
        """;

    public const string PostgresWriterRecipe = """
        CREATE ROLE "{{name}}" LOGIN PASSWORD '{{password}}' VALID UNTIL '{{expiration}}+00' IN ROLE pg_read_all_data, pg_write_all_data;
        """;

    // Idempotent. The backend has already stopped the role signing in, so ending its sessions here
    // ends them for good; anything the role created goes to the target's login, and its grants go,
    // as DROP ROLE requires.
    public const string PostgresRevocation = """
        DO $$
        BEGIN
            IF EXISTS (SELECT FROM pg_roles WHERE rolname = '{{name}}') THEN
                PERFORM pg_terminate_backend(pid, 5000) FROM pg_stat_activity WHERE usename = '{{name}}';
                REASSIGN OWNED BY "{{name}}" TO CURRENT_USER;
                DROP OWNED BY "{{name}}";
                DROP ROLE "{{name}}";
            END IF;
        END
        $$;
        """;

    /// <summary>
    /// Dynamic-secret recipes, two on SQL Server and two on PostgreSQL. They mint real principals on the
    /// configured targets, so the sample roles are genuinely usable: issue one and you can connect with it.
    /// The SQL Server pair seeds an empty table; each PostgreSQL role is added by name when missing.
    ///
    /// Roles are seeded rather than authored in the UI on purpose — the statements run with the
    /// vault's backend rights, so defining one is a privileged, out-of-band act.
    /// </summary>
    private static async Task SeedDynamicSecretRolesAsync(EclipsVaultDbContext db, ILogger logger, DateTimeOffset now)
    {
        // {{name}} and {{password}} are substituted by CredentialStatementTemplate, which refuses any
        // value that is not strictly alphanumeric — the reason interpolating into DDL is safe here.
        const string createReader = """
            CREATE LOGIN [{{name}}] WITH PASSWORD = '{{password}}';
            CREATE USER [{{name}}] FOR LOGIN [{{name}}];
            ALTER ROLE db_datareader ADD MEMBER [{{name}}];
            """;

        const string createWriter = """
            CREATE LOGIN [{{name}}] WITH PASSWORD = '{{password}}';
            CREATE USER [{{name}}] FOR LOGIN [{{name}}];
            ALTER ROLE db_datareader ADD MEMBER [{{name}}];
            ALTER ROLE db_datawriter ADD MEMBER [{{name}}];
            """;

        // Idempotent, and kills live sessions first: DROP LOGIN fails while the principal is
        // connected, and a lease that cannot be reclaimed is the failure mode that matters.
        const string revoke = """
            DECLARE @sessions nvarchar(max) = N'';
            SELECT @sessions = @sessions + N'KILL ' + CAST(session_id AS nvarchar(10)) + N';'
            FROM sys.dm_exec_sessions WHERE login_name = N'{{name}}';
            IF LEN(@sessions) > 0 EXEC sp_executesql @sessions;
            DROP USER IF EXISTS [{{name}}];
            IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{{name}}') DROP LOGIN [{{name}}];
            """;

        DynamicSecretRole[] sqlServerSamples =
        [
            new DynamicSecretRole
            {
                Id = Guid.NewGuid(),
                Name = "phoenix_db_reader",
                Description = "Read-only SQL Server login on the vault database, minted on demand.",
                ProjectKey = "PHOENIX",
                Environment = SecretEnvironment.Development,
                Sensitivity = SensitivityLevel.Internal,
                Backend = DynamicSecretBackend.SqlServer,
                CreationStatements = createReader,
                RevocationStatements = revoke,
                DefaultTtlMinutes = 15,
                MaxTtlMinutes = 60,
                IsEnabled = true,
                CreatedAtUtc = now
            },
            new DynamicSecretRole
            {
                Id = Guid.NewGuid(),
                Name = "global_db_writer",
                Description = "Read/write SQL Server login on the vault database. Short leases only.",
                ProjectKey = "GLOBAL",
                Environment = SecretEnvironment.Production,
                Sensitivity = SensitivityLevel.Secret,
                Backend = DynamicSecretBackend.SqlServer,
                CreationStatements = createWriter,
                RevocationStatements = revoke,
                DefaultTtlMinutes = 10,
                MaxTtlMinutes = 30,
                IsEnabled = true,
                CreatedAtUtc = now
            }
        ];

        DynamicSecretRole[] postgresSamples =
        [
            new DynamicSecretRole
            {
                Id = Guid.NewGuid(),
                Name = "phoenix_pg_reader",
                Description = "Read-only PostgreSQL role on the PostgreSQL target, minted on demand.",
                ProjectKey = "PHOENIX",
                Environment = SecretEnvironment.Development,
                Sensitivity = SensitivityLevel.Internal,
                Backend = DynamicSecretBackend.Postgres,
                CreationStatements = PostgresReaderRecipe,
                RevocationStatements = PostgresRevocation,
                DefaultTtlMinutes = 15,
                MaxTtlMinutes = 60,
                IsEnabled = true,
                CreatedAtUtc = now
            },
            new DynamicSecretRole
            {
                Id = Guid.NewGuid(),
                Name = "global_pg_writer",
                Description = "Read/write PostgreSQL role on the PostgreSQL target. Short leases only.",
                ProjectKey = "GLOBAL",
                Environment = SecretEnvironment.Production,
                Sensitivity = SensitivityLevel.Secret,
                Backend = DynamicSecretBackend.Postgres,
                CreationStatements = PostgresWriterRecipe,
                RevocationStatements = PostgresRevocation,
                DefaultTtlMinutes = 10,
                MaxTtlMinutes = 30,
                IsEnabled = true,
                CreatedAtUtc = now
            }
        ];

        // The SQL Server pair seeds an empty table only, so a sample a developer deleted stays deleted. The
        // PostgreSQL pair came later, so it is added by name to a table seeded before it existed.
        var missing = new List<DynamicSecretRole>();
        if (!await db.DynamicSecretRoles.AnyAsync())
        {
            missing.AddRange(sqlServerSamples);
        }

        foreach (var sample in postgresSamples)
        {
            // Compared by the database, so its own collation decides whether the name is taken.
            if (!await db.DynamicSecretRoles.AnyAsync(role => role.Name == sample.Name))
            {
                missing.Add(sample);
            }
        }

        if (missing.Count == 0)
        {
            return;
        }

        db.DynamicSecretRoles.AddRange(missing);
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {RoleCount} dynamic-secret roles: {RoleNames}", missing.Count, missing.Select(role => role.Name));
    }

    private static async Task<Secret> SealSecretAsync(
        ICryptoEngine engine,
        DateTimeOffset now,
        string name,
        string projectKey,
        SecretEnvironment environment,
        SensitivityLevel sensitivity,
        string value,
        DateTimeOffset? expiresAtUtc = null,
        bool isHoneyToken = false)
    {
        var id = Guid.NewGuid();
        var sealedSecret = await engine.SealAsync(
            System.Text.Encoding.UTF8.GetBytes(value), SecretBinding.ForCurrentValue(id), CancellationToken.None);
        return new Secret
        {
            Id = id,
            Name = name,
            ProjectKey = projectKey,
            Environment = environment,
            Sensitivity = sensitivity,
            Ciphertext = sealedSecret.Ciphertext,
            WrappedDek = sealedSecret.WrappedDek,
            KekId = sealedSecret.KekId,
            Algorithm = sealedSecret.Algorithm,
            IsHoneyToken = isHoneyToken,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc
        };
    }
}
