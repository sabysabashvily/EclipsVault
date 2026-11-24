using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EclipsVault.Tests.DynamicSecrets;

/// <summary>
/// The PostgreSQL backend mints on its own target and refuses without one, as the SQL Server backend does.
/// A fallback to the vault's connection, or to the SQL Server target, would quietly hand CREATEROLE to a
/// login the operator never granted it to.
/// </summary>
public class PostgresBackendTargetTests
{
    private static PostgresBackend Backend(
        string? postgresTarget,
        string? sqlServerTarget = null,
        ILogger<PostgresBackend>? logger = null,
        string? vaultPostgres = null) =>
        new(Options.Create(new DynamicSecretTargetOptions
        {
            PostgresTargetConnectionString = postgresTarget,
            TargetConnectionString = sqlServerTarget,
        }), TimeProvider.System, logger ?? NullLogger<PostgresBackend>.Instance, new VaultPostgresConnection(vaultPostgres));

    /// <summary>Keeps every message the backend logs, as its log sinks would receive it.</summary>
    private sealed class RecordingLogger : ILogger<PostgresBackend>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add($"{formatter(state, exception)} {exception}");
    }

    private static DynamicSecretRole Role() => new()
    {
        Id = Guid.NewGuid(),
        Name = "reporting_ro",
        Backend = DynamicSecretBackend.Postgres,
        CreationStatements = """CREATE ROLE "{{name}}" LOGIN PASSWORD '{{password}}';""",
        RevocationStatements = """DROP ROLE IF EXISTS "{{name}}";""",
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Minting_refuses_when_no_postgres_target_is_configured_and_reports_that_nothing_was_sent(string? connectionString)
        => await Assert.ThrowsAsync<MintRefusedException>(() =>
            Backend(connectionString).MintAsync(Role(), "svc_reporting", "Abc123", DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None));

    [Fact]
    public async Task Minting_refuses_when_only_the_sql_server_target_is_configured()
        => await Assert.ThrowsAsync<MintRefusedException>(() =>
            Backend(null, "Server=app-db;Database=app;Encrypt=True").MintAsync(
                Role(), "svc_reporting", "Abc123", DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None));

    [Fact]
    public async Task Minting_on_an_unreachable_target_reports_that_nothing_was_sent()
        => await Assert.ThrowsAsync<MintRefusedException>(() =>
            Backend("Host=127.0.0.1;Port=1;Timeout=1").MintAsync(
                Role(), "svc_reporting", "Abc123", DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None));

    [Fact]
    public async Task Revoking_refuses_when_no_postgres_target_is_configured_and_names_the_setting()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Backend(null).RevokeAsync(Role(), "svc_reporting", CancellationToken.None));

        Assert.Contains("DynamicSecrets:PostgresTargetConnectionString", ex.Message, StringComparison.Ordinal);
        Assert.Contains("CREATEROLE", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Host=app-pg;Port=not-a-port;Password=Pw4Target9")]
    [InlineData("Host=app-pg;Colour=blue;Password=Pw4Target9")]
    [InlineData("Database=app;Password=Pw4Target9")]
    [InlineData("Host=app-pg:notaport;Password=Pw4Target9")]
    public async Task A_malformed_target_refuses_minting_as_nothing_sent_and_logs_the_setting_without_its_value(string connectionString)
    {
        var logger = new RecordingLogger();

        await Assert.ThrowsAsync<MintRefusedException>(() =>
            Backend(connectionString, logger: logger).MintAsync(
                Role(), "svc_reporting", "Abc123", DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None));

        Assert.NotEmpty(logger.Messages);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("Pw4Target9", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_malformed_target_fails_revoking_without_logging_its_value()
    {
        var logger = new RecordingLogger();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            Backend("Host=app-pg;Colour=blue;Password=Pw4Target9", logger: logger).RevokeAsync(Role(), "svc_reporting", CancellationToken.None));

        Assert.DoesNotContain(logger.Messages, message => message.Contains("Pw4Target9", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Host=vault-pg;Port=5432;Database=vault;Password=VaultPw1", "Host=vault-pg;Port=5432;Database=app;Password=TargetPw2")]
    [InlineData("Host=VAULT-PG.;Database=vault;Password=VaultPw1", "Host=vault-pg;Port=5432;Database=app;Password=TargetPw2")]
    [InlineData("Host=[::ffff:127.0.0.1]:5432;Database=vault;Password=VaultPw1", "Host=127.0.0.1;Port=5432;Database=app;Password=TargetPw2")]
    [InlineData("Host=pg-a:5432,pg-b:5433;Database=vault;Password=VaultPw1", "Host=pg-b:5433;Database=app;Password=TargetPw2")]
    [InlineData("Host=/var/run/postgresql;Port=5432;Database=vault;Password=VaultPw1", "Host=/var/run/postgresql/;Database=app;Password=TargetPw2")]
    public async Task Minting_refuses_when_the_target_shares_an_endpoint_with_the_vaults_postgres_cluster_and_logs_no_passwords(
        string vaultConnection, string targetConnection)
    {
        var logger = new RecordingLogger();

        await Assert.ThrowsAsync<MintRefusedException>(() =>
            Backend(targetConnection, logger: logger, vaultPostgres: vaultConnection).MintAsync(
                Role(), "svc_reporting", "Abc123", DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None));

        Assert.Contains(logger.Messages, message =>
            message.Contains("DynamicSecrets:PostgresTargetConnectionString", StringComparison.Ordinal)
            && message.Contains("own PostgreSQL cluster", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("VaultPw1", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("TargetPw2", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Host=127.0.0.1;Port=1;Database=vault", "Host=[::1]:1;Database=app;Timeout=1")]
    [InlineData("Host=/nonexistent-eclipsvault-A;Port=5432;Database=vault", "Host=/nonexistent-eclipsvault-a;Port=5432;Database=app;Timeout=1")]
    [InlineData("Host=/nonexistent-eclipsvault-sock;Port=5432;Database=vault", "Host=/nonexistent-eclipsvault-sock;Port=5433;Database=app;Timeout=1")]
    public async Task Distinct_loopback_addresses_and_case_distinct_unix_sockets_do_not_collide_statically(
        string vaultConnection, string targetConnection)
    {
        var logger = new RecordingLogger();

        await Assert.ThrowsAsync<MintRefusedException>(() =>
            Backend(targetConnection, logger: logger, vaultPostgres: vaultConnection).MintAsync(
                Role(), "svc_reporting", "Abc123", DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None));

        Assert.DoesNotContain(logger.Messages, message => message.Contains("own PostgreSQL cluster", StringComparison.Ordinal));
    }

    [Fact]
    public void Vault_postgres_connection_to_string_never_exposes_the_connection_string()
    {
        var holder = new VaultPostgresConnection("Host=vault-pg;Password=TopSecretVaultPassword");

        Assert.DoesNotContain("TopSecretVaultPassword", holder.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Minting_refuses_fail_closed_when_the_vault_postgres_connection_cannot_be_parsed_without_logging_it()
    {
        var logger = new RecordingLogger();

        await Assert.ThrowsAsync<MintRefusedException>(() =>
            Backend("Host=app-pg;Port=5432;Password=TargetPw2", logger: logger, vaultPostgres: "Host=vault-pg;Colour=blue;Password=VaultPw1")
                .MintAsync(Role(), "svc_reporting", "Abc123", DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None));

        Assert.DoesNotContain(logger.Messages, message => message.Contains("VaultPw1", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("TargetPw2", StringComparison.Ordinal));
    }
}
