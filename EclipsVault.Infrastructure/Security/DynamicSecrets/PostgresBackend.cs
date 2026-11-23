using EclipsVault.Core.Application.DynamicSecrets;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EclipsVault.Infrastructure.Security;

/// <summary>
/// The PostgreSQL backend: mints and destroys short-lived login roles on the PostgreSQL target configured
/// in <see cref="DynamicSecretTargetOptions.PostgresTargetConnectionString"/>, never on the vault's own
/// database, for the reason <see cref="DynamicSecretTargetOptions"/> gives.
///
/// As on SQL Server, the statements are DDL and cannot take parameters, so credentials are rendered in
/// as text through <see cref="CredentialStatementTemplate"/>, which refuses anything but letters, digits
/// and '_'. A role's statements must double-quote <c>"{{name}}"</c>: the minted name has capitals, and
/// PostgreSQL folds an unquoted identifier to lower case, while a sign-in matches the name exactly.
///
/// One command runs a role's statements as one implicit transaction, so a recipe that fails part-way
/// leaves nothing behind; the service still runs the revocation after such a failure.
/// </summary>
public sealed class PostgresBackend : IDynamicSecretBackend
{
    private const string NotConfiguredMessage =
        "PostgreSQL dynamic secrets need a target database of their own. Set " +
        $"'{DynamicSecretTargetOptions.SectionName}:PostgresTargetConnectionString' to the PostgreSQL database whose " +
        "roles this vault should manage, using a login that holds CREATEROLE there. It is deliberately not " +
        "the vault's own connection: granting that privilege to the vault's login would let a compromise of " +
        "this application reach the server that stores the audit trail.";

    private readonly string? _connectionString;
    private readonly ILogger<PostgresBackend> _logger;

    public PostgresBackend(IOptions<DynamicSecretTargetOptions> target, ILogger<PostgresBackend> logger)
    {
        _connectionString = target.Value.PostgresTargetConnectionString;
        _logger = logger;
    }

    public DynamicSecretBackend Backend => DynamicSecretBackend.Postgres;

    public async Task MintAsync(
        DynamicSecretRole role, string identity, string password, DateTimeOffset expiresAtUtc, CancellationToken ct)
    {
        var sql = CredentialStatementTemplate.Render(role.CreationStatements, identity, password, expiresAtUtc);
        await ExecuteOnTargetAsync(sql, ct, _ => new MintRefusedException(role.Name));

        _logger.LogInformation(
            "Minted PostgreSQL role {Identity} for role {RoleName}; lease elapses at {ExpiresAtUtc}",
            identity, role.Name, expiresAtUtc);
    }

    public async Task RevokeAsync(DynamicSecretRole role, string identity, CancellationToken ct)
    {
        // Revocation needs no password, but the template insists on a renderable one.
        var sql = CredentialStatementTemplate.Render(role.RevocationStatements, identity, "unused", DateTimeOffset.UnixEpoch);
        await ExecuteOnTargetAsync(sql, ct);

        _logger.LogInformation("Dropped PostgreSQL role {Identity} for role {RoleName}", identity, role.Name);
    }

    /// <summary>
    /// Runs statements on the PostgreSQL target over a connection of their own, refusing when none is
    /// configured. When <paramref name="nothingSent"/> is given, a failure before the statements reached the
    /// target throws what it returns instead, so the caller knows there is nothing to undo.
    /// </summary>
    private async Task ExecuteOnTargetAsync(string sql, CancellationToken ct, Func<Exception, Exception>? nothingSent = null)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            _logger.LogError(NotConfiguredMessage);
            var notConfigured = new InvalidOperationException(NotConfiguredMessage);
            throw nothingSent?.Invoke(notConfigured) ?? notConfigured;
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(ct);
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            if (nothingSent is null)
            {
                throw;
            }

            throw nothingSent(ex);
        }

        try
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            throw;
        }
    }

    // Only the error's type and SQLSTATE: a server message can quote the statement, and a mint statement holds the password.
    private void LogFailure(Exception ex)
        => _logger.LogWarning("A statement on the PostgreSQL dynamic-secret target failed: {ExceptionType}, SQLSTATE {SqlState}",
            ex.GetType().Name, (ex as PostgresException)?.SqlState);
}
