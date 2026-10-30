using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// One short-lived Legacy passkey ceremony per browser session and purpose. Replacing cancels the
/// previous attempt; taking removes it atomically even when two nodes receive the same answer.
/// State expires absolutely after 120 seconds, without renewal on reads or other session activity.
/// </summary>
public interface IPasskeyCeremonyStore
{
    Task ReplaceAsync(string sessionId, PasskeyCeremonyPurpose purpose, string state, CancellationToken ct);

    Task<string?> TakeAsync(string sessionId, PasskeyCeremonyPurpose purpose, CancellationToken ct);
}
