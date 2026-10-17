using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EclipsVault.Infrastructure.Persistence;

/// <summary>
/// The one place standalone audit rows are written. Hands the entry to
/// <see cref="AuditGroupCommitter"/> and waits until it is durably committed and chained; any
/// failure is escalated to a critical log and rethrown as <see cref="AuditWriteFailedException"/> so
/// the caller aborts (fail-closed).
///
/// The waiting is the contract, not an implementation detail: a reveal is audited <em>before</em>
/// its value is decrypted, so this returning is what makes the row's existence a precondition of the
/// plaintext. Rows are committed in groups because the chain lock is what caps the read path (see
/// the committer), but no caller is ever released early.
/// </summary>
public sealed class AuditSink : IAuditSink
{
    private readonly AuditGroupCommitter _committer;
    private readonly IAuditContext _actor;
    private readonly TimeProvider _clock;
    private readonly ILogger<AuditSink> _logger;

    public AuditSink(
        AuditGroupCommitter committer, IAuditContext actor, TimeProvider clock, ILogger<AuditSink> logger)
    {
        _committer = committer;
        _actor = actor;
        _clock = clock;
        _logger = logger;
    }

    public async Task WriteAsync(AuditEntry entry, CancellationToken ct)
    {
        // Stamped here, not at flush time: this is when the event happened, and this is the scope
        // that knows who caused it.
        var row = new AuditLog
        {
            Id = Guid.NewGuid(),
            TimestampUtc = _clock.GetUtcNow(),
            UserId = entry.ActorUserId ?? (entry.InheritActorUserId ? _actor.UserId : null),
            Username = entry.ActorUsername ?? _actor.Username ?? "system",
            SourceIp = _actor.SourceIp ?? "internal",
            Action = entry.Action,
            ResourceType = entry.ResourceType,
            ResourceId = entry.ResourceId,
            ResourceName = entry.ResourceName,
            Details = entry.Details,
            IsCritical = entry.IsCritical
        };

        try
        {
            await _committer.CommitAsync(row, ct);
        }
        catch (AuditWriteFailedException)
        {
            throw; // the committer already logged the batch failure and its cause
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogCritical(ex,
                "Audit write failed for {AuditAction} on {ResourceType} {ResourceId} — operation aborted (fail-closed)",
                entry.Action, entry.ResourceType, entry.ResourceId);

            throw new AuditWriteFailedException(
                $"The audit trail could not be persisted for '{entry.Action}' on {entry.ResourceType} '{entry.ResourceId}'. " +
                "The operation was aborted before any secret material was released (fail-closed).", ex);
        }
    }
}
