using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace EclipsVault.Infrastructure.Security;

/// <summary>
/// Active-defence playbook for honey-token trips: revoke the caller's sessions,
/// blacklist the source range, raise a critical structured alert, and persist a
/// high-priority audit row. Runs to completion even if individual steps fail —
/// the alarm must always sound.
/// </summary>
public sealed class IntrusionResponseService : IIntrusionResponseService
{
    private readonly IAuditSink _audit;
    private readonly IIpBlacklist _blacklist;
    private readonly ISessionRevocationService _revocation;
    private readonly IAuditContext _actor;
    private readonly ILogger<IntrusionResponseService> _logger;

    public IntrusionResponseService(
        IAuditSink audit,
        IIpBlacklist blacklist,
        ISessionRevocationService revocation,
        IAuditContext actor,
        ILogger<IntrusionResponseService> logger)
    {
        _audit = audit;
        _blacklist = blacklist;
        _revocation = revocation;
        _actor = actor;
        _logger = logger;
    }

    public async Task TriggerHoneyTokenAsync(Guid secretId, string secretName, CancellationToken ct)
    {
        var userId = _actor.UserId;
        var sourceIp = _actor.SourceIp;

        if (userId is { } id)
        {
            await _revocation.RevokeAsync(id, ct);
        }

        if (!string.IsNullOrEmpty(sourceIp))
        {
            await _blacklist.BlockAsync(sourceIp, $"Honey-token '{secretName}' tripped", ct);
        }

        _logger.LogCritical(
            "SECURITY ALERT: honey-token {SecretName} ({SecretId}) was requested by user {UserId} ({Username}) from {SourceIp}. " +
            "Sessions revoked and source range blacklisted.",
            secretName, secretId, userId, _actor.Username ?? "anonymous", sourceIp ?? "unknown");

        try
        {
            await _audit.WriteAsync(new AuditEntry
            {
                Action = AuditAction.HoneyTokenTripped,
                ResourceType = nameof(Secret),
                ResourceId = secretId,
                ResourceName = secretName,
                Details = "Session revoked; source IP range blacklisted",
                IsCritical = true,

                // The trap can be tripped before a principal is resolved, so name the actor
                // explicitly rather than letting the sink record its "system" default.
                ActorUsername = _actor.Username ?? "anonymous"
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The containment actions above already ran and the critical alert is in the
            // structured log; a failed audit insert must not un-trip the trap. This is the one
            // place that deliberately swallows the sink's fail-closed AuditWriteFailedException.
            _logger.LogCritical(ex, "Failed to persist honey-token audit row for secret {SecretId}", secretId);
        }
    }
}
