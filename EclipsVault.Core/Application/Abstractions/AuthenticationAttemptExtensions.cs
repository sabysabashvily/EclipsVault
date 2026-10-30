using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Abstractions;

public static class AuthenticationAttemptExtensions
{
    public static void RecordUserEvent(this IAuthenticationAttempt attempt, AuditAction action, User account, string? details)
        => attempt.Record(new AuditEntry
        {
            Action = action,
            ResourceType = nameof(User),
            ResourceId = account.Id,
            ActorUserId = account.Id,
            ActorUsername = account.Username,
            InheritActorUserId = false,
            Details = details
        });
}
