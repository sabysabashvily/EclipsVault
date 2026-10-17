using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Abstractions;

/// <summary>Convenience writers over <see cref="IAuditSink"/> for the recurring audit shapes.</summary>
public static class AuditSinkExtensions
{
    /// <summary>
    /// Writes a User-scoped authentication or self-service event attributed to the affected account.
    /// Administrative User* actions on another account must supply their actor separately instead
    /// of using the target account as the actor. Centralising this shape here keeps the
    /// <see cref="AuditEntry"/> field mapping in one place.
    /// </summary>
    public static Task WriteUserEventAsync(
        this IAuditSink audit, AuditAction action, Guid? userId, string username, string? details, CancellationToken ct)
        => audit.WriteAsync(new AuditEntry
        {
            Action = action,
            ResourceType = "User",
            ResourceId = userId,
            ActorUserId = userId,
            InheritActorUserId = false,
            ActorUsername = username,
            Details = details
        }, ct);
}
