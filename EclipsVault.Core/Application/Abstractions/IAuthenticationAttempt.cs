using EclipsVault.Core.Domain.Entities;

namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// Reloads the account under serialization, then commits the attempt's proof consumption,
/// counters and recorded events together. A rejected attempt may still count and record a failure.
/// Nothing staged by a failed transaction can escape through a later save.
/// </summary>
public interface IAuthenticationAttempt
{
    Task<T> RunAsync<T>(User account, Func<CancellationToken, Task<T>> attempt, CancellationToken ct);

    /// <summary>Records an event in the running attempt; it is durable only when RunAsync succeeds.</summary>
    void Record(AuditEntry entry);
}
