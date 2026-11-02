using EclipsVault.Core.Domain.Exceptions;

namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// Starts a change to one service account or its API keys. The change runs in a transaction that holds
/// the service account's lock, so changes to one service account take effect one at a time and each
/// reads what the one before it committed. Read the service account and its keys after it begins.
/// </summary>
public interface IServiceAccountChanges
{
    /// <exception cref="AuditWriteFailedException">The lock could not be taken; nothing was changed.</exception>
    Task<IServiceAccountChange> BeginAsync(Guid serviceAccountId, CancellationToken ct);
}

/// <summary>
/// A change in progress. Disposing it without committing saves nothing and records nothing, and
/// discards what it staged.
/// </summary>
public interface IServiceAccountChange : IAsyncDisposable
{
    /// <summary>Saves everything staged since the change began, together with its one audit event.</summary>
    /// <exception cref="ServiceAccountNameTakenException">Another service account took the new one's name first; nothing was saved.</exception>
    /// <exception cref="AuditWriteFailedException">The change and its event could not be stored; neither was.</exception>
    Task CommitAsync(AuditEntry entry, CancellationToken ct);
}
