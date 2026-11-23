namespace EclipsVault.Core.Application.Abstractions;

/// <summary>
/// The blocks a break-glass recovery removed, held under the source's guard. The guard keeps the
/// source refused until the lift is settled here; no timeout, lift or listing releases it.
/// </summary>
public interface IRecoveryLift
{
    /// <summary>Whether any block was removed.</summary>
    bool Lifted { get; }

    /// <summary>The ranges whose blocks were removed.</summary>
    IReadOnlyList<string> Removed { get; }

    /// <summary>What holds the source refused while the guard stays, for an operator to reconcile.</summary>
    string Guard { get; }

    /// <summary>The removal stands, because its row committed or nothing was removed: releases the guard.</summary>
    Task ReleaseAsync();

    /// <summary>
    /// The removal must not stand: puts back exactly what was removed, keeping blocks raised meanwhile,
    /// and releases the guard only when every removed block is confirmed back as it was. Never throws.
    /// </summary>
    /// <returns>Whether the restoration was confirmed; when it was not, the guard stays.</returns>
    Task<bool> TryRestoreAsync();
}
