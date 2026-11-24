using EclipsVault.Core.Application.DynamicSecrets;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EclipsVault.Infrastructure.Security;

/// <summary>
/// The SQL Server backend, for both kinds of credential the vault owns:
/// <list type="bullet">
/// <item><see cref="IDynamicSecretBackend"/> — mints and destroys short-lived principals of its own.</item>
/// <item><see cref="IManagedSecretBackend"/> — re-passwords a principal that already exists.</item>
/// </list>
/// One class because it is one connection to one server; the two ports stay separate because a
/// backend could plausibly do one and not the other.
///
/// Everything here is DDL, which cannot be parameterised, so credentials are rendered in as text —
/// safe only because <see cref="CredentialStatementTemplate"/> refuses to render anything that is
/// not strictly alphanumeric. No operator-supplied text is ever interpolated.
///
/// The server it talks to is the <em>managed target</em> configured in
/// <see cref="DynamicSecretTargetOptions"/> — never the vault's own database. Running these statements
/// as the vault's own login would require it to hold <c>ALTER ANY LOGIN</c>, a privilege that can
/// re-password any principal on that instance, including on the server storing the audit trail. That
/// would void the least-privilege property the threat model relies on to bound an application compromise.
/// </summary>
public sealed class SqlServerBackend : IDynamicSecretBackend, IManagedSecretBackend
{
    /// <summary>
    /// Fixed, not operator-supplied: rotating a managed secret must only ever change a password.
    /// A per-secret statement would make every managed secret a place to hide arbitrary SQL.
    /// </summary>
    private const string RotateStatement = "ALTER LOGIN [{{name}}] WITH PASSWORD = '{{password}}';";

    /// <summary>
    /// The errors with which SQL Server refuses <c>ALTER LOGIN</c> before changing anything: no such
    /// login or no permission (15151, 15247), and a password its policy rejects (15114 to 15118).
    /// Any other error, a cancellation or a trigger's message among them, may follow a change that held.
    /// </summary>
    private static readonly HashSet<int> PasswordUnchangedErrors = [15151, 15247, 15114, 15115, 15116, 15117, 15118];

    /// <summary>
    /// Fixed, not operator-supplied, and committed before sessions are drained: a disabled login
    /// cannot open a new session while existing ones are killed and confirmed closed, and stays shut
    /// if the subsequent drop fails.
    /// </summary>
    private const string DisableSignInStatement =
        "IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{{name}}') ALTER LOGIN [{{name}}] DISABLE;";

    /// <summary>
    /// Errors SQL Server raises when a session already closed between reading <c>sys.dm_exec_sessions</c>
    /// and running <c>KILL</c>: not an active process (6106) or system/own process race (6104, 6107).
    /// </summary>
    private static readonly HashSet<int> SessionAlreadyGoneErrors = [6104, 6106, 6107];

    private static readonly TimeSpan SessionDrainTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SessionPollInterval = TimeSpan.FromMilliseconds(100);

    private const string NotConfiguredMessage =
        "Dynamic secrets and managed rotation need a target database of their own. Set " +
        $"'{DynamicSecretTargetOptions.SectionName}:TargetConnectionString' to the database whose " +
        "logins this vault should manage, using a login that holds ALTER ANY LOGIN there. It is " +
        "deliberately not the vault's own connection: granting that privilege to the vault's login " +
        "would let a compromise of this application take over the server that stores the audit trail.";

    private readonly DynamicSecretTargetOptions _target;
    private readonly TimeProvider _clock;
    private readonly ILogger<SqlServerBackend> _logger;

    public SqlServerBackend(
        IOptions<DynamicSecretTargetOptions> target,
        ILogger<SqlServerBackend> logger,
        TimeProvider? clock = null)
    {
        _target = target.Value;
        _clock = clock ?? TimeProvider.System;
        _logger = logger;
    }

    /// <summary>
    /// Opens a connection to the <em>managed target</em>, refusing when none is configured.
    ///
    /// Never the vault's connection: see <see cref="DynamicSecretTargetOptions"/>. Refuses rather
    /// than falling back, because a fallback would silently require the vault's own login to hold
    /// <c>ALTER ANY LOGIN</c> — a privilege that, on the server holding the audit trail, turns an
    /// application compromise into control of the evidence.
    ///
    /// When <paramref name="nothingSent"/> is given, a failure before anything reached the target
    /// throws what it returns for that failure instead, so the caller knows there is nothing to undo.
    /// </summary>
    private async Task<SqlConnection> OpenTargetAsync(
        CancellationToken ct, Func<Exception, Exception>? nothingSent = null)
    {
        if (!_target.IsConfigured)
        {
            _logger.LogError(NotConfiguredMessage);
            var notConfigured = new InvalidOperationException(NotConfiguredMessage);
            throw nothingSent?.Invoke(notConfigured) ?? notConfigured;
        }

        var connection = new SqlConnection(_target.TargetConnectionString);
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

    private async Task RunAsync(SqlConnection connection, string sql, CancellationToken ct)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            throw;
        }
    }

    private async Task ExecuteOnTargetAsync(
        string sql, CancellationToken ct, Func<Exception, Exception>? nothingSent = null)
    {
        await using var connection = await OpenTargetAsync(ct, nothingSent);
        await RunAsync(connection, sql, ct);
    }

    // A server message can quote the statement, and a mint statement holds the password.
    private void LogFailure(Exception ex)
        => _logger.LogWarning("A statement on the dynamic-secret target failed: {ExceptionType}, error number {ErrorNumber}",
            ex.GetType().Name, (ex as SqlException)?.Number);

    public DynamicSecretBackend Backend => DynamicSecretBackend.SqlServer;

    public async Task MintAsync(
        DynamicSecretRole role, string identity, string password, DateTimeOffset expiresAtUtc, CancellationToken ct)
    {
        var sql = CredentialStatementTemplate.Render(role.CreationStatements, identity, password, expiresAtUtc);
        await ExecuteOnTargetAsync(sql, ct, _ => new MintRefusedException(role.Name));

        _logger.LogInformation(
            "Minted SQL Server login {Identity} for role {RoleName}; lease elapses at {ExpiresAtUtc}",
            identity, role.Name, expiresAtUtc);
    }

    /// <summary>
    /// Ends the credential: disables sign-in, kills every active session for the login and polls
    /// <c>sys.dm_exec_sessions</c> until none remains, runs the role's revocation statements, and
    /// confirms the login is gone. Throws, leaving the login disabled, if sessions cannot be confirmed
    /// closed within the bounded wait or if revocation leaves the login behind.
    /// </summary>
    public async Task RevokeAsync(DynamicSecretRole role, string identity, CancellationToken ct)
    {
        // The password is irrelevant to revocation, but the template demands a renderable one —
        // so pass a placeholder that satisfies the same guard rather than weakening it.
        var disableSignIn = CredentialStatementTemplate.Render(DisableSignInStatement, identity, "unused", DateTimeOffset.UnixEpoch);
        var revocation = CredentialStatementTemplate.Render(role.RevocationStatements, identity, "unused", DateTimeOffset.UnixEpoch);

        await using var connection = await OpenTargetAsync(ct);
        await RunAsync(connection, disableSignIn, ct);
        await EndSessionsAndConfirmClosedAsync(connection, identity, ct);
        await RunAsync(connection, revocation, ct);

        if (await LoginExistsAsync(connection, identity, ct))
        {
            _logger.LogWarning("The revocation statements of role {RoleName} left SQL Server login {Identity} in place", role.Name, identity);
            throw new InvalidOperationException("The revocation statements did not drop the SQL Server login.");
        }

        _logger.LogInformation("Dropped SQL Server login {Identity} for role {RoleName}", identity, role.Name);
    }

    private async Task EndSessionsAndConfirmClosedAsync(SqlConnection connection, string identity, CancellationToken ct)
    {
        var started = _clock.GetTimestamp();
        var killed = new HashSet<short>();

        while (true)
        {
            var activeSessions = await ActiveSessionsAsync(connection, identity, ct);
            if (activeSessions.Count == 0)
            {
                return;
            }

            foreach (var sessionId in activeSessions)
            {
                if (killed.Add(sessionId))
                {
                    await KillSessionAsync(connection, sessionId, ct);
                }
            }

            if (_clock.GetElapsedTime(started) >= SessionDrainTimeout)
            {
                _logger.LogWarning(
                    "SQL Server login {Identity} still has {SessionCount} open session(s) after the revocation drain deadline",
                    identity, activeSessions.Count);
                throw new InvalidOperationException("A session of the revoked SQL Server login is still open.");
            }

            await Task.Delay(SessionPollInterval, _clock, ct);
        }
    }

    private static async Task<List<short>> ActiveSessionsAsync(SqlConnection connection, string identity, CancellationToken ct)
    {
        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT session_id FROM sys.dm_exec_sessions WHERE login_name = @login AND session_id <> @@SPID";
        query.Parameters.AddWithValue("@login", identity);

        var sessions = new List<short>();
        await using var rows = await query.ExecuteReaderAsync(ct);
        while (await rows.ReadAsync(ct))
        {
            sessions.Add(rows.GetInt16(0));
        }

        return sessions;
    }

    private async Task KillSessionAsync(SqlConnection connection, short sessionId, CancellationToken ct)
    {
        try
        {
            await using var kill = connection.CreateCommand();
            kill.CommandText = $"KILL {sessionId};";
            await kill.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException ex) when (SessionAlreadyGoneErrors.Contains(ex.Number))
        {
            // Session already closed between reading sys.dm_exec_sessions and issuing KILL.
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            throw;
        }
    }

    private static async Task<bool> LoginExistsAsync(SqlConnection connection, string identity, CancellationToken ct)
    {
        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT 1 FROM sys.server_principals WHERE name = @login";
        query.Parameters.AddWithValue("@login", identity);
        return await query.ExecuteScalarAsync(ct) is not null;
    }

    /// <summary>
    /// Throws <see cref="UpstreamRotationRefusedException"/> only when the password certainly did not
    /// change: the statement was never sent, or the server refused it with an error that leaves the
    /// password as it was. Any other failure
    /// (a timeout, a lost connection, a cancelled call) leaves it unknown whether the server ran it.
    /// </summary>
    public async Task RotatePrincipalAsync(string principal, string newPassword, CancellationToken ct)
    {
        string sql;
        try
        {
            sql = CredentialStatementTemplate.Render(RotateStatement, principal, newPassword, DateTimeOffset.UnixEpoch);
        }
        catch (Exception notSent)
        {
            throw new UpstreamRotationRefusedException(notSent);
        }

        try
        {
            await ExecuteOnTargetAsync(sql, ct, notSent => new UpstreamRotationRefusedException(notSent));
        }
        catch (SqlException refused) when (PasswordUnchangedErrors.Contains(refused.Number))
        {
            throw new UpstreamRotationRefusedException(refused);
        }

        // Never log the password — only that the principal moved.
        _logger.LogInformation("Rotated the password of SQL Server login {Principal}", principal);
    }
}
