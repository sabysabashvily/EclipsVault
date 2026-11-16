namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// A managed rotation may have changed the login's password, and the vault could not bring the login
/// and its stored value back into step: the old password could not be put back, or whether the new
/// one was stored could not be read, so the two are not known to agree. The message, the spec's drift
/// sentence for the worst case, is safe to show and is also the critical audit row's details. The
/// inner exception is the failure that left their agreement unconfirmed.
/// </summary>
public sealed class UpstreamRotationDriftedException : DomainException
{
    public UpstreamRotationDriftedException(string principal, bool recorded, Exception cause)
        : base(DriftSentence(principal), cause)
        => Recorded = recorded;

    /// <summary>Whether the critical drift row was written. When it was not, only a log line can say so.</summary>
    public bool Recorded { get; }

    public static string DriftSentence(string principal)
        => $"The password of '{principal}' was changed upstream but could not be stored or put back. " +
           "The stored value no longer opens it — reconcile by hand.";
}
