namespace EclipsVault.Core.Application.Secrets;

/// <summary>
/// The command palette's secret search, run over rows the caller may already list.
///
/// It is the Secrets list with a substring match on top, and that is the point: given only the list's
/// rows (decoys already dropped, the access filter already applied), a search can only ever return
/// names the caller was entitled to enumerate. Enumeration is not audited per row, matching every
/// other list; opening a result is.
/// </summary>
public static class SecretSearch
{
    /// <summary>Enough for the palette to show a useful shortlist; not a bulk-export channel.</summary>
    public const int ResultLimit = 8;

    // A one-letter query is not a search, it is a request for the whole list.
    private const int MinimumQueryLength = 2;

    public static IReadOnlyList<SecretSummaryDto> Find(IEnumerable<SecretSummaryDto> listable, string? query)
    {
        var needle = (query ?? string.Empty).Trim();
        if (needle.Length < MinimumQueryLength)
        {
            return [];
        }

        return listable
            .Where(secret => secret.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                             || secret.ProjectKey.Contains(needle, StringComparison.OrdinalIgnoreCase))
            // A name that starts with what was typed is likelier the one meant than one that merely contains it.
            .OrderByDescending(secret => secret.Name.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
            .ThenBy(secret => secret.Name, StringComparer.OrdinalIgnoreCase)
            .Take(ResultLimit)
            .ToList();
    }
}
