using System.Globalization;
using System.Net;
using Npgsql;

namespace EclipsVault.Infrastructure.Security;

/// <summary>
/// The vault's own PostgreSQL connection string when the vault runs on PostgreSQL, or null on SQL Server.
/// Held separately so <see cref="PostgresBackend"/> can confirm its dynamic-secret target is a different
/// PostgreSQL cluster without taking a dependency on EF Core or configuration binders.
/// </summary>
public sealed record VaultPostgresConnection(string? ConnectionString);

/// <summary>
/// Confirms that a PostgreSQL dynamic-secret target is not the vault's own PostgreSQL cluster.
/// PostgreSQL roles and <c>pg_read_all_data</c>/<c>pg_write_all_data</c> span every database in a cluster,
/// so pointing dynamic secrets at another database on the vault's server would expose the vault's tables.
/// </summary>
internal static class PostgresClusterGuard
{
    private const int DefaultPort = 5432;
    private const string CanonicalLoopback = "localhost";

    /// <summary>
    /// True when the two PostgreSQL connection strings name any common <c>(host, port)</c> endpoint,
    /// after normalising case, trailing DNS dots, IPv6 brackets, default port 5432, and loopback aliases.
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
    /// Reads the cluster's immutable 64-bit <c>system_identifier</c> from <c>pg_control_system()</c>,
    /// which uniquely identifies an initialised PostgreSQL cluster regardless of hostnames, proxies,
    /// port forwards, or database names.
    /// </summary>
    public static async Task<long> ReadSystemIdentifierAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand("SELECT system_identifier FROM pg_control_system()", connection);
        var value = await query.ExecuteScalarAsync(ct);
        if (value is null or DBNull)
        {
            throw new InvalidOperationException("PostgreSQL returned no system_identifier from pg_control_system().");
        }

        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static HashSet<(string Host, int Port)> EndpointsOf(NpgsqlConnectionStringBuilder builder)
    {
        var defaultPort = builder.Port > 0 ? builder.Port : DefaultPort;
        var rawHost = builder.Host;
        var result = new HashSet<(string Host, int Port)>();
        if (string.IsNullOrWhiteSpace(rawHost))
        {
            return result;
        }

        foreach (var segment in rawHost.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParseEndpoint(segment, defaultPort, out var endpoint))
            {
                result.Add(endpoint);
            }
        }

        return result;
    }

    private static bool TryParseEndpoint(string segment, int defaultPort, out (string Host, int Port) endpoint)
    {
        endpoint = default;
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        string hostPart;
        var port = defaultPort;

        if (segment.StartsWith('['))
        {
            var close = segment.IndexOf(']');
            if (close < 0)
            {
                return false;
            }

            hostPart = segment[1..close];
            var rest = segment[(close + 1)..];
            if (rest.StartsWith(':') && int.TryParse(rest[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort))
            {
                port = parsedPort;
            }
        }
        else
        {
            var firstColon = segment.IndexOf(':');
            var lastColon = segment.LastIndexOf(':');
            // Exactly one colon means host:port; multiple colons mean a bare IPv6 literal without a port.
            if (firstColon > 0 && firstColon == lastColon
                && int.TryParse(segment[(lastColon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort))
            {
                hostPart = segment[..lastColon];
                port = parsedPort;
            }
            else
            {
                hostPart = segment;
            }
        }

        var normalized = NormalizeHost(hostPart);
        if (normalized.Length == 0)
        {
            return false;
        }

        endpoint = (normalized, port);
        return true;
    }

    private static string NormalizeHost(string host)
    {
        var trimmed = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (trimmed == CanonicalLoopback)
        {
            return CanonicalLoopback;
        }

        if (IPAddress.TryParse(trimmed, out var ip))
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }

            return IPAddress.IsLoopback(ip) ? CanonicalLoopback : ip.ToString().ToLowerInvariant();
        }

        return trimmed;
    }
}
