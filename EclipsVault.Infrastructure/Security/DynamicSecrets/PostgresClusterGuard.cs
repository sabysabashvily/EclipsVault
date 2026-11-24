using System.Globalization;
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
    private const int DefaultPort = 5432;

    /// <summary>
    /// True when the two PostgreSQL connection strings name any common endpoint, after normalising DNS case,
    /// trailing DNS dots, IPv6 brackets, IPv4-mapped IPv6 addresses, and default port 5432 while preserving
    /// case-sensitive Unix socket paths and distinct loopback addresses (aliases are resolved live via
    /// <c>pg_catalog.pg_control_system()</c>).
    /// </summary>
    public static bool SharesEndpoint(NpgsqlConnectionStringBuilder vault, NpgsqlConnectionStringBuilder target)
    {
        var vaultEndpoints = EndpointsOf(vault);
        if (vaultEndpoints.Count == 0)
        {
            return false;
        }

        foreach (var endpoint in EndpointsOf(target))
        {
            if (vaultEndpoints.Contains(endpoint))
            {
                return true;
            }
        }

        return false;
    }

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
        if (string.IsNullOrWhiteSpace(builder.Host))
        {
            return [connectionString];
        }

        var defaultPort = builder.Port > 0 ? builder.Port : DefaultPort;
        var segments = builder.Host.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length <= 1)
        {
            return [connectionString];
        }

        var expanded = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            var (hostPart, port) = SplitMultiHostSegment(segment, defaultPort);
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

    private static HashSet<PostgresEndpoint> EndpointsOf(NpgsqlConnectionStringBuilder builder)
    {
        var defaultPort = builder.Port > 0 ? builder.Port : DefaultPort;
        var rawHost = builder.Host;
        var result = new HashSet<PostgresEndpoint>();
        if (string.IsNullOrWhiteSpace(rawHost))
        {
            return result;
        }

        var segments = rawHost.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var isMultiHost = segments.Length > 1;
        foreach (var segment in segments)
        {
            if (TryParseEndpoint(segment, defaultPort, isMultiHost, out var endpoint))
            {
                result.Add(endpoint);
            }
        }

        return result;
    }

    private static bool TryParseEndpoint(string segment, int defaultPort, bool isMultiHost, out PostgresEndpoint endpoint)
    {
        endpoint = default;
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        var trimmed = segment.Trim();
        var (hostPart, port) = isMultiHost
            ? SplitMultiHostSegment(trimmed, defaultPort)
            : SplitSingleHostSegment(trimmed, defaultPort);

        if (hostPart.Length == 0)
        {
            return false;
        }

        if (hostPart.StartsWith('@'))
        {
            endpoint = new PostgresEndpoint(IsUnixSocket: true, hostPart, port);
            return true;
        }

        if (hostPart.StartsWith('/'))
        {
            var socketPath = hostPart.Length > 1 ? hostPart.TrimEnd('/') : hostPart;
            endpoint = new PostgresEndpoint(IsUnixSocket: true, socketPath, port);
            return true;
        }

        var normalized = NormalizeTcpHost(hostPart);
        if (normalized.Length == 0)
        {
            return false;
        }

        endpoint = new PostgresEndpoint(IsUnixSocket: false, normalized, port);
        return true;
    }

    /// <summary>
    /// Matches Npgsql's <c>NpgsqlMultiHostDataSource</c> parsing: bracketed IPv6 splits at <c>]</c>,
    /// while any other entry (including Unix socket paths) splits an inline <c>:port</c> at its colon.
    /// </summary>
    private static (string Host, int Port) SplitMultiHostSegment(string segment, int defaultPort)
    {
        var trimmed = segment.Trim();
        if (trimmed.StartsWith('['))
        {
            var close = trimmed.IndexOf(']');
            if (close < 0)
            {
                return (string.Empty, defaultPort);
            }

            var host = trimmed[1..close];
            var rest = trimmed[(close + 1)..];
            if (rest.StartsWith(':') && int.TryParse(rest[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var bracketPort))
            {
                return (host, bracketPort);
            }

            return (host, defaultPort);
        }

        var firstColon = trimmed.IndexOf(':');
        var lastColon = trimmed.LastIndexOf(':');
        if (trimmed.StartsWith('/') || trimmed.StartsWith('@'))
        {
            if (lastColon > 0 && int.TryParse(trimmed[(lastColon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var socketPort))
            {
                return (trimmed[..lastColon], socketPort);
            }

            return (trimmed, defaultPort);
        }

        // Exactly one colon means host:port; multiple colons mean a bare IPv6 literal without a port.
        if (firstColon > 0 && firstColon == lastColon
            && int.TryParse(trimmed[(lastColon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort))
        {
            return (trimmed[..lastColon], parsedPort);
        }

        return (trimmed, defaultPort);
    }

    /// <summary>
    /// Matches Npgsql's single-host <c>NpgsqlConnectionStringBuilder</c> parsing: Unix socket paths and
    /// abstract socket names keep any colons verbatim and always use <see cref="NpgsqlConnectionStringBuilder.Port"/>.
    /// </summary>
    private static (string Host, int Port) SplitSingleHostSegment(string segment, int defaultPort)
    {
        var trimmed = segment.Trim();
        if (trimmed.StartsWith('/') || trimmed.StartsWith('@'))
        {
            return (trimmed, defaultPort);
        }

        return SplitMultiHostSegment(trimmed, defaultPort);
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

    private readonly record struct PostgresEndpoint(bool IsUnixSocket, string Host, int Port);
}
