using EclipsVault.Core.Domain.Entities;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// Saves one change to a vault account together with the one audit event that describes it, in one
/// transaction, so neither is stored without the other. The change is everything staged since the
/// account was loaded: edits to the loaded account and the rows repositories staged for it.
/// </summary>
public interface IAccountChangeCommitter
{
    /// <summary>
    /// Saves the staged change and records <paramref name="action"/> for it, attributed to the account.
    /// Refuses when another change to the account was saved after <paramref name="account"/> was loaded.
    /// </summary>
    /// <returns>False when nothing was staged: nothing changed, so nothing is recorded.</returns>
    /// <exception cref="AccountChangedException">The account changed after it was loaded; nothing was saved.</exception>
    /// <exception cref="EmailAddressTakenException">Another account took the staged email first; nothing was saved.</exception>
    /// <exception cref="AuditWriteFailedException">The change and its event could not be stored; neither was.</exception>
    Task<bool> CommitAsync(User account, AuditAction action, string details, CancellationToken ct);

    /// <summary>Preserves explicit event attribution for an administrator changing another account.</summary>
    Task<bool> CommitAsync(User account, AuditEntry entry, CancellationToken ct);
}
