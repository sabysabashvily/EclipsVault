using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Application.Auditing;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Networks;

public static class BreakGlassLiftAudit
{
    private const string ResourceType = "TrustedNetwork";
    private const string DetailsPrefix = "Intrusion-defence block lifted by break-glass recovery: ";

    public static AuditEntry Create(UserDto user, IReadOnlyList<string> removed)
    {
        var ranges = string.Join(", ", removed.Order(StringComparer.Ordinal));
        return new AuditEntry
        {
            Action = AuditAction.IpRangeUnblocked,
            ResourceType = ResourceType,
            ResourceName = ranges.Length <= AuditLog.ResourceNameMaxLength ? ranges : $"{removed.Count} IP ranges",
            ActorUserId = user.Id,
            ActorUsername = user.Username,
            Details = DetailsPrefix + ranges
        };
    }

    public static string? ReadRanges(AuditEntryDto row)
    {
        if (row.Action != AuditAction.IpRangeUnblocked || row.ResourceType != ResourceType
            || row.Details is not { } details || !details.StartsWith(DetailsPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        // Imported and older audit rows may carry free text. My Activity only shows canonical CIDRs.
        var ranges = details[DetailsPrefix.Length..];
        return ranges.Split(", ").All(range => NetworkRules.TryParseCidr(range, out var canonical) && range == canonical)
            ? ranges
            : null;
    }
}
