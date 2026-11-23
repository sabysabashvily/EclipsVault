using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Infrastructure.Security;
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
    private static PostgresBackend Backend(string? postgresTarget, string? sqlServerTarget = null) =>
        new(Options.Create(new DynamicSecretTargetOptions
        {
            PostgresTargetConnectionString = postgresTarget,
            TargetConnectionString = sqlServerTarget,
        }), NullLogger<PostgresBackend>.Instance);

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
}
