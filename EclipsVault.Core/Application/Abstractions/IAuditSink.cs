using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// One entry to append to the immutable audit trail. The actor (who did it) and the
/// source IP are supplied by the sink from the ambient request context. Authentication
/// overrides the actor with the account being authenticated, even if the request already
/// carries another session, and explicitly records no account for an unknown username.
/// </summary>
public sealed record AuditEntry
{
    public required AuditAction Action { get; init; }

    public required string ResourceType { get; init; }

    public Guid? ResourceId { get; init; }

    public string? ResourceName { get; init; }

    public string? Details { get; init; }

    public bool IsCritical { get; init; }

    /// <summary>Overrides the recorded actor id (used for auth events with no authenticated principal).</summary>
    public Guid? ActorUserId { get; init; }

    /// <summary>
    /// Falls back to the ambient actor id when no override is supplied. Authentication sets this
    /// to false so an explicitly unknown account cannot inherit an existing session's account id.
    /// </summary>
    public bool InheritActorUserId { get; init; } = true;

    /// <summary>Overrides the recorded actor name (used for auth events with no authenticated principal).</summary>
    public string? ActorUsername { get; init; }
}

/// <summary>
/// Fail-closed writer for the immutable audit trail — the single place audit rows are
/// created. Implementations MUST throw <c>AuditWriteFailedException</c> when the entry
/// cannot be persisted, so the calling operation aborts before any secret material is
/// released.
/// </summary>
public interface IAuditSink
{
    Task WriteAsync(AuditEntry entry, CancellationToken ct);
}
