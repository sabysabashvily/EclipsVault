using System.Net;
using EclipsVault.Core.Application.Networks;
using Npgsql;

namespace EclipsVault.Infrastructure.Security;

/// <summary>
/// The vault's own PostgreSQL connection string when the vault runs on PostgreSQL, or null on SQL Server.
/// Held separately so <see cref="PostgresBackend"/> can confirm its dynamic-secret target is a different
/// PostgreSQL cluster without taking a dependency on EF Core or configuration binders.
/// </summary>
public sealed class VaultPostgresConnection(string? connectionString)
{
    public string? ConnectionString { get; } = connectionString;

    public override string ToString() => nameof(VaultPostgresConnection);
}

/// <summary>
/// Confirms that a PostgreSQL dynamic-secret target is not the vault's own PostgreSQL cluster.
/// PostgreSQL roles and <c>pg_read_all_data</c>/<c>pg_write_all_data</c> span every database in a cluster,
/// so pointing dynamic secrets at another database on the vault's server would expose the vault's tables.
/// </summary>
internal static class PostgresClusterGuard
{
    /// <summary>
    /// Reads the cluster's immutable 64-bit <c>system_identifier</c> from
    /// <c>pg_catalog.pg_control_system()</c>, which uniquely identifies an initialised PostgreSQL cluster
    /// regardless of hostnames, proxies, port forwards, <c>search_path</c>, or database names.
    /// </summary>
    public static async Task<long> ReadSystemIdentifierAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        CancellationToken ct)
    {
        await using var query = new NpgsqlCommand(
            "SELECT system_identifier FROM pg_catalog.pg_control_system()",
            connection,
            transaction);
        var value = await query.ExecuteScalarAsync(ct);
        if (value is not long systemIdentifier)
        {
            throw new InvalidOperationException("PostgreSQL did not return an int64 system_identifier from pg_catalog.pg_control_system().");
        }

        return systemIdentifier;
    }

    /// <summary>
    /// Expands a PostgreSQL connection string into one single-endpoint connection string per host listed in
    /// <c>Host</c>, so the live cluster check probes every configured vault endpoint rather than only the
    /// first host Npgsql happens to reach. Clears <c>Target Session Attributes</c> on the single-host probe
    /// strings because Npgsql rejects multi-host session attributes such as <c>read-write</c> on single-host
    /// connections, and splits inline <c>:port</c> suffixes onto <see cref="NpgsqlConnectionStringBuilder.Port"/>
    /// (including for Unix sockets) to match Npgsql's multi-host parser.
    /// </summary>
    public static IReadOnlyList<string> ExpandHostConnectionStrings(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var segments = HostSegments(builder);
        if (segments.Length == 1)
        {
            return [connectionString];
        }

        var expanded = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            var (hostPart, port) = SplitHostPort(segment, builder.Port);
            var single = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Host = hostPart,
                Port = port,
            };
            single.Remove("Target Session Attributes");
            expanded.Add(single.ConnectionString);
        }

        return expanded;
    }

    /// <summary>
    /// Parses and normalizes the endpoints in a connection string for comparison. Aliases are resolved
    /// separately by comparing cluster identities on live connections.
    /// </summary>
    public static HashSet<PostgresEndpoint> ReadEndpoints(NpgsqlConnectionStringBuilder builder)
    {
        var segments = HostSegments(builder);
        var result = new HashSet<PostgresEndpoint>();
        var isMultiHost = segments.Length > 1;
        foreach (var segment in segments)
        {
            var (host, port) = isMultiHost || !IsUnixSocket(segment)
                ? SplitHostPort(segment, builder.Port)
                : (segment, builder.Port);
            result.Add(NormalizeEndpoint(host, port));
        }

        return result;
    }

    private static string[] HostSegments(NpgsqlConnectionStringBuilder builder)
    {
        var host = builder.Host ?? string.Empty;
        var segments = host.Contains(',') ? host.Split(',', StringSplitOptions.TrimEntries) : [host];
        // Dropping an empty entry could skip a local endpoint that the driver tries.
        if (Array.Exists(segments, string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("A PostgreSQL connection string has an empty host entry.");
        }

        return segments;
    }

    private static PostgresEndpoint NormalizeEndpoint(string host, int port)
    {
        if (host.StartsWith('@'))
        {
            return new PostgresEndpoint(IsUnixSocket: true, host, port);
        }

        if (Path.IsPathRooted(host))
        {
            var socketPath = host.Length > 1 ? host.TrimEnd('/') : host;
            return new PostgresEndpoint(IsUnixSocket: true, socketPath, port);
        }

        return new PostgresEndpoint(IsUnixSocket: false, NormalizeTcpHost(host), port);
    }

    private static bool IsUnixSocket(string host) => host.StartsWith('@') || Path.IsPathRooted(host);

    private static (string Host, int Port) SplitHostPort(string host, int defaultPort)
    {
        var portSeparator = host.LastIndexOf(':');
        if (portSeparator < 0)
        {
            return (host, defaultPort);
        }

        // Npgsql applies the same closing-bracket rule to every multi-host entry, including socket paths.
        var previousColon = host.AsSpan(0, portSeparator).LastIndexOf(':');
        var closingBracket = host.LastIndexOf(']');
        var hasPort = previousColon < 0 || (previousColon < closingBracket && closingBracket < portSeparator);
        if (!hasPort)
        {
            return (host, defaultPort);
        }

        if (host.AsSpan(0, portSeparator).IsWhiteSpace()
            || !int.TryParse(host.AsSpan(portSeparator + 1), out var port) || port <= 0)
        {
            throw new InvalidOperationException("A PostgreSQL connection string has an invalid host or port.");
        }

        return (host[..portSeparator], port);
    }

    private static string NormalizeTcpHost(string host)
    {
        var trimmed = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (IPAddress.TryParse(trimmed, out var ip))
        {
            return NetworkRules.Normalize(ip).ToString().ToLowerInvariant();
        }

        return trimmed;
    }

    internal readonly record struct PostgresEndpoint(bool IsUnixSocket, string Host, int Port);
}
