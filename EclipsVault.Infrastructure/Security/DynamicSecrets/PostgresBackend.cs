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
    private const string SettingName = $"{DynamicSecretTargetOptions.SectionName}:PostgresTargetConnectionString";

    /// <summary>How long after a revocation to look again for a session that was still starting up.</summary>
    private static readonly TimeSpan StartupGrace = TimeSpan.FromSeconds(1);

    private const string NotConfiguredMessage =
        "PostgreSQL dynamic secrets need a target database of their own. Set " +
        $"'{SettingName}' to the PostgreSQL database whose " +
        "roles this vault should manage, using a login that holds CREATEROLE there. It is deliberately not " +
        "the vault's own connection: granting that privilege to the vault's login would let a compromise of " +
        "this application reach the server that stores the audit trail.";

    /// <summary>
    /// Fixed, not operator-supplied, and committed before the role's own revocation runs: a role that cannot
    /// sign in can't open a session between that revocation ending sessions and its drop committing, and
    /// stays shut if the drop then fails.
    /// </summary>
    private const string DisableSignInStatement = """
        DO $$
        BEGIN
            IF EXISTS (SELECT FROM pg_roles WHERE rolname = '{{name}}') THEN
                ALTER ROLE "{{name}}" NOLOGIN;
            END IF;
        END
        $$;
        """;

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
        await using var connection = await OpenTargetAsync(ct, _ => new MintRefusedException(role.Name));
        await RunAsync(connection, sql, ct);

        _logger.LogInformation(
            "Minted PostgreSQL role {Identity} for role {RoleName}; lease elapses at {ExpiresAtUtc}",
            identity, role.Name, expiresAtUtc);
    }

    /// <summary>
    /// Each step is its own command, so its own transaction: sign-in is off and committed before the role's
    /// statements run, and the check for live sessions afterwards reads the server afresh rather than the
    /// statistics snapshot PostgreSQL keeps for the length of a transaction.
    /// </summary>
    public async Task RevokeAsync(DynamicSecretRole role, string identity, CancellationToken ct)
    {
        // Revocation needs no password, but the template insists on a renderable one.
        var disableSignIn = CredentialStatementTemplate.Render(DisableSignInStatement, identity, "unused", DateTimeOffset.UnixEpoch);
        var revocation = CredentialStatementTemplate.Render(role.RevocationStatements, identity, "unused", DateTimeOffset.UnixEpoch);

        await using var connection = await OpenTargetAsync(ct);
        var roleId = await RoleIdAsync(connection, identity, ct);
        await RunAsync(connection, disableSignIn, ct);
        await RunAsync(connection, revocation, ct);
        if (roleId is { } minted)
        {
            await RequireNoSessionsAsync(connection, minted, identity, ct);
        }

        _logger.LogInformation("Dropped PostgreSQL role {Identity} for role {RoleName}", identity, role.Name);
    }

    /// <summary>
    /// Throws when a session of the role is still open. Dropping a role leaves its sessions running, and
    /// <c>pg_terminate_backend</c> reports a session it could not end only by returning false, so this
    /// looks for itself. The second look is after <see cref="StartupGrace"/>: PostgreSQL 17 lists a
    /// connection only once its startup has finished, and one that passed the sign-in check just before
    /// <c>NOLOGIN</c> committed would otherwise be missed.
    /// </summary>
    private async Task RequireNoSessionsAsync(NpgsqlConnection connection, uint roleId, string identity, CancellationToken ct)
    {
        foreach (var wait in new[] { TimeSpan.Zero, StartupGrace })
        {
            await Task.Delay(wait, ct);
            await using var count = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE usesysid = @role", connection);
            count.Parameters.AddWithValue("role", NpgsqlTypes.NpgsqlDbType.Oid, roleId);
            if ((long)(await count.ExecuteScalarAsync(ct))! > 0)
            {
                _logger.LogWarning("PostgreSQL role {Identity} still has an open session after its revocation", identity);
                throw new InvalidOperationException("A session of the revoked PostgreSQL role is still open.");
            }
        }
    }

    private static async Task<uint?> RoleIdAsync(NpgsqlConnection connection, string identity, CancellationToken ct)
    {
        await using var lookUp = new NpgsqlCommand("SELECT oid FROM pg_roles WHERE rolname = @name", connection);
        lookUp.Parameters.AddWithValue("name", identity);
        return await lookUp.ExecuteScalarAsync(ct) as uint?;
    }

    /// <summary>
    /// Opens the PostgreSQL target, refusing when none is configured. When <paramref name="nothingSent"/> is
    /// given, a failure before anything reached the target throws what it returns instead, so the caller
    /// knows there is nothing to undo.
    /// </summary>
    private async Task<NpgsqlConnection> OpenTargetAsync(CancellationToken ct, Func<Exception, Exception>? nothingSent = null)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            _logger.LogError(NotConfiguredMessage);
            var notConfigured = new InvalidOperationException(NotConfiguredMessage);
            throw nothingSent?.Invoke(notConfigured) ?? notConfigured;
        }

        NpgsqlConnection connection;
        try
        {
            // Npgsql parses the setting here, so a malformed one fails before anything is sent.
            connection = new NpgsqlConnection(_connectionString);
        }
        catch (Exception ex)
        {
            // The value holds the target's password, and a parse error can quote it.
            _logger.LogError("'{Setting}' is not a valid PostgreSQL connection string: {ExceptionType}",
                SettingName, ex.GetType().Name);
            if (nothingSent is null)
            {
                throw;
            }

            throw nothingSent(ex);
        }

        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch (Exception ex)
        {
            await connection.DisposeAsync();
            LogFailure(ex);
            if (nothingSent is null)
            {
                throw;
            }

            throw nothingSent(ex);
        }
    }

    private async Task RunAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
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
