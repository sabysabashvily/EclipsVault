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

    /// <summary>
    /// How long past the target's <c>authentication_timeout</c> to wait for a connection still starting up.
    /// A startup transaction begins just before authentication, which the server ends at that timeout, so
    /// what remains is ordinary startup work, or a client's own <c>post_auth_delay</c>.
    /// </summary>
    private static readonly TimeSpan StartupMargin = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan StartupPollInterval = TimeSpan.FromMilliseconds(100);

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
    private readonly TimeProvider _clock;
    private readonly ILogger<PostgresBackend> _logger;

    public PostgresBackend(IOptions<DynamicSecretTargetOptions> target, TimeProvider clock, ILogger<PostgresBackend> logger)
    {
        _clock = clock;
        _connectionString = target.Value.PostgresTargetConnectionString;
        _logger = logger;
    }

    public DynamicSecretBackend Backend => DynamicSecretBackend.Postgres;

    public async Task MintAsync(
        DynamicSecretRole role, string identity, string password, DateTimeOffset expiresAtUtc, CancellationToken ct)
    {
        var verifier = ScramSha256Verifier.Create(password);
        var sql = CredentialStatementTemplate.RenderWithScramVerifier(role.CreationStatements, identity, verifier, expiresAtUtc);
        await using var connection = await OpenTargetAsync(ct, _ => new MintRefusedException(role.Name));
        await RunAsync(connection, sql, ct);

        _logger.LogInformation(
            "Minted PostgreSQL role {Identity} for role {RoleName}; lease elapses at {ExpiresAtUtc}",
            identity, role.Name, expiresAtUtc);
    }

    /// <summary>
    /// Ends the credential: turns its sign-in off, waits out any connection that may have signed in just
    /// before, ends the role's sessions and confirms none is left, and only then runs the role's statements,
    /// which drop it. Each step is its own command, so its own transaction, and every check reads the server
    /// afresh rather than the activity snapshot PostgreSQL keeps for the length of a transaction.
    ///
    /// Throws, leaving the role in place and unable to sign in, when any of that cannot be confirmed. A role
    /// that is already gone was dropped by a revocation that confirmed it had no sessions, so revoking it
    /// again succeeds without doing anything.
    /// </summary>
    public async Task RevokeAsync(DynamicSecretRole role, string identity, CancellationToken ct)
    {
        // Revocation needs no password, but the template insists on a renderable one.
        var disableSignIn = CredentialStatementTemplate.Render(DisableSignInStatement, identity, "unused", DateTimeOffset.UnixEpoch);
        var revocation = CredentialStatementTemplate.Render(role.RevocationStatements, identity, "unused", DateTimeOffset.UnixEpoch);

        await using var connection = await OpenTargetAsync(ct);
        if (await RoleIdAsync(connection, identity, ct) is not { } roleId)
        {
            _logger.LogInformation("PostgreSQL role {Identity} for role {RoleName} was already dropped", identity, role.Name);
            return;
        }

        await RunAsync(connection, disableSignIn, ct);
        await AwaitStartupsAsync(connection, identity, ct);
        await EndSessionsAsync(connection, roleId, identity, ct);
        await RunAsync(connection, revocation, ct);
        if (await RoleIdAsync(connection, identity, ct) is not null)
        {
            _logger.LogWarning("The revocation statements of role {RoleName} left PostgreSQL role {Identity} in place", role.Name, identity);
            throw new InvalidOperationException("The revocation statements did not drop the PostgreSQL role.");
        }

        _logger.LogInformation("Dropped PostgreSQL role {Identity} for role {RoleName}", identity, role.Name);
    }

    /// <summary>
    /// Waits until every connection that began starting up before sign-in was turned off shows its role.
    ///
    /// PostgreSQL checks a connection's right to sign in inside its startup transaction, and records the
    /// connection's role in <c>pg_stat_activity</c> only near that transaction's end: PostgreSQL 17 lists
    /// the connection only then, and PostgreSQL 18 lists it earlier with no role (<c>usesysid</c> null). A
    /// client may also hold it between the check and that record with <c>post_auth_delay</c>. So a connection
    /// that passed the check before <c>NOLOGIN</c> committed may not show its role yet. Every startup
    /// transaction holds its virtual transaction lock, though, which <c>pg_locks</c> shows with its process
    /// id. This samples those locks once, then the activity, and follows each lock whose process showed no
    /// role until the lock is released (its startup ended, so its role is recorded by now, or it is gone) or
    /// the process shows a role. Locks taken later belong to connections that check sign-in after
    /// <c>NOLOGIN</c> committed, so they are not followed, and sessions showing a role are never waited on.
    /// </summary>
    private async Task AwaitStartupsAsync(NpgsqlConnection connection, string identity, CancellationToken ct)
    {
        var limit = await AuthenticationTimeoutAsync(connection, ct) + StartupMargin;
        var started = _clock.GetTimestamp();
        var startups = await VirtualTransactionsAsync(connection, ct);
        startups.ExceptWith(await RolePublishedAsync(connection, startups, ct));
        while (startups.Count > 0)
        {
            if (_clock.GetElapsedTime(started) > limit)
            {
                _logger.LogWarning(
                    "Revoking PostgreSQL role {Identity}: {StartupCount} connection(s) still starting up could belong to it",
                    identity, startups.Count);
                throw new InvalidOperationException("A connection still starting up may belong to the revoked PostgreSQL role.");
            }

            await Task.Delay(StartupPollInterval, _clock, ct);
            var held = await VirtualTransactionsAsync(connection, ct);
            startups.IntersectWith(held);
            startups.ExceptWith(await RolePublishedAsync(connection, startups, ct));
        }
    }

    /// <summary>The granted virtual transaction locks of live processes, as (process id, virtual transaction id).</summary>
    private static async Task<HashSet<(int Pid, string VirtualXid)>> VirtualTransactionsAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand(
            "SELECT pid, virtualxid FROM pg_locks WHERE locktype = 'virtualxid' AND granted AND pid IS NOT NULL", connection);
        var locks = new HashSet<(int, string)>();
        await using var rows = await query.ExecuteReaderAsync(ct);
        while (await rows.ReadAsync(ct))
        {
            locks.Add((rows.GetInt32(0), rows.GetString(1)));
        }
        return locks;
    }

    /// <summary>Those of <paramref name="locks"/> whose process <c>pg_stat_activity</c> lists with a role now.</summary>
    private static async Task<IEnumerable<(int Pid, string VirtualXid)>> RolePublishedAsync(
        NpgsqlConnection connection, HashSet<(int Pid, string VirtualXid)> locks, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand("SELECT pid FROM pg_stat_activity WHERE pid = ANY(@pids) AND usesysid IS NOT NULL", connection);
        query.Parameters.AddWithValue("pids", locks.Select(held => held.Pid).ToArray());
        var listed = new HashSet<int>();
        await using var rows = await query.ExecuteReaderAsync(ct);
        while (await rows.ReadAsync(ct))
        {
            listed.Add(rows.GetInt32(0));
        }
        return locks.Where(held => listed.Contains(held.Pid)).ToList();
    }

    private static async Task<TimeSpan> AuthenticationTimeoutAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand("SELECT setting::int FROM pg_settings WHERE name = 'authentication_timeout'", connection);
        return TimeSpan.FromSeconds((int)(await query.ExecuteScalarAsync(ct))!);
    }

    /// <summary>
    /// Ends the role's sessions and confirms with a fresh look that none is left. The role can no longer sign
    /// in and no connection that could be its own is still starting, so none can appear afterwards.
    /// </summary>
    private async Task EndSessionsAsync(NpgsqlConnection connection, uint roleId, string identity, CancellationToken ct)
    {
        await using (var end = new NpgsqlCommand("SELECT pg_terminate_backend(pid, 5000) FROM pg_stat_activity WHERE usesysid = @role", connection))
        {
            end.Parameters.AddWithValue("role", NpgsqlTypes.NpgsqlDbType.Oid, roleId);
            await end.ExecuteNonQueryAsync(ct);
        }

        await using var count = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE usesysid = @role", connection);
        count.Parameters.AddWithValue("role", NpgsqlTypes.NpgsqlDbType.Oid, roleId);
        if ((long)(await count.ExecuteScalarAsync(ct))! > 0)
        {
            _logger.LogWarning("PostgreSQL role {Identity} still has an open session after its sessions were ended", identity);
            throw new InvalidOperationException("A session of the revoked PostgreSQL role is still open.");
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
