namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Break-glass recovery recorded its sign-in but could not settle the block lift, so the source's guard
/// stays and the source remains refused until an operator reconciles the blocks and the trail.
/// </summary>
public sealed class RecoveryIncompleteException(string? guard = null) : DomainException(FailureMessage)
{
    /// <summary>What still holds the source refused, for the operator's log; never shown to the caller.</summary>
    public string? Guard { get; } = guard;

    public const string FailureMessage =
        "Your sign-in was recorded, but the vault could not finish lifting the block, so this network stays blocked. " +
        "From another network, an administrator can release it under Administration → Networks.";
}
