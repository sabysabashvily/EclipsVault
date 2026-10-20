using EclipsVault.Core.Application.DynamicSecrets;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
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
/// <see cref="DynamicSecretTargetOptions"/> — never the vault's own database. It used to run these
/// statements over the vault's <c>DbContext</c>, which quietly required the vault's own login to
/// hold <c>ALTER ANY LOGIN</c>: a privilege that can re-password any principal on that instance,
/// including on the server storing the audit trail. That made enabling this feature silently void
/// the least-privilege property the threat model relies on to bound an application compromise.
/// </summary>
public sealed class SqlServerBackend : IDynamicSecretBackend, IManagedSecretBackend
{
    /// <summary>
    /// Fixed, not operator-supplied: rotating a managed secret must only ever change a password.
    /// A per-secret statement would make every managed secret a place to hide arbitrary SQL.
    /// </summary>
    private const string RotateStatement = "ALTER LOGIN [{{name}}] WITH PASSWORD = '{{password}}';";

    private readonly DynamicSecretTargetOptions _target;
    private readonly ILogger<SqlServerBackend> _logger;

    public SqlServerBackend(IOptions<DynamicSecretTargetOptions> target, ILogger<SqlServerBackend> logger)
    {
        _target = target.Value;
        _logger = logger;
    }

    /// <summary>
    /// Runs one DDL statement against the <em>managed target</em>, on its own connection.
    ///
    /// Never the vault's connection: see <see cref="DynamicSecretTargetOptions"/>. Refuses rather
    /// than falling back, because a fallback would silently require the vault's own login to hold
    /// <c>ALTER ANY LOGIN</c> — a privilege that, on the server holding the audit trail, turns an
    /// application compromise into control of the evidence.
    /// </summary>
    private async Task ExecuteOnTargetAsync(string sql, CancellationToken ct)
    {
        if (!_target.IsConfigured)
        {
            throw new InvalidOperationException(
                $"Dynamic secrets and managed rotation need a target database of their own. Set " +
                $"'{DynamicSecretTargetOptions.SectionName}:TargetConnectionString' to the database whose " +
                "logins this vault should manage, using a login that holds ALTER ANY LOGIN there. It is " +
                "deliberately not the vault's own connection: granting that privilege to the vault's login " +
                "would let a compromise of this application take over the server that stores the audit trail.");
        }

        await using var connection = new SqlConnection(_target.TargetConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    public DynamicSecretBackend Backend => DynamicSecretBackend.SqlServer;

    public async Task MintAsync(
        DynamicSecretRole role, string identity, string password, DateTimeOffset expiresAtUtc, CancellationToken ct)
    {
        var sql = CredentialStatementTemplate.Render(role.CreationStatements, identity, password, expiresAtUtc);
        await ExecuteOnTargetAsync(sql, ct);

        _logger.LogInformation(
            "Minted SQL Server login {Identity} for role {RoleName}; lease elapses at {ExpiresAtUtc}",
            identity, role.Name, expiresAtUtc);
    }

    public async Task RevokeAsync(DynamicSecretRole role, string identity, CancellationToken ct)
    {
        // The password is irrelevant to revocation, but the template demands a renderable one —
        // so pass a placeholder that satisfies the same guard rather than weakening it.
        var sql = CredentialStatementTemplate.Render(role.RevocationStatements, identity, "unused", DateTimeOffset.UnixEpoch);
        await ExecuteOnTargetAsync(sql, ct);

        _logger.LogInformation("Dropped SQL Server login {Identity} for role {RoleName}", identity, role.Name);
    }

    public async Task RotatePrincipalAsync(string principal, string newPassword, CancellationToken ct)
    {
        var sql = CredentialStatementTemplate.Render(RotateStatement, principal, newPassword, DateTimeOffset.UnixEpoch);
        await ExecuteOnTargetAsync(sql, ct);

        // Never log the password — only that the principal moved.
        _logger.LogInformation("Rotated the password of SQL Server login {Principal}", principal);
    }
}
