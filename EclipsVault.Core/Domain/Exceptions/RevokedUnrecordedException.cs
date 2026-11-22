namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>The backend destroyed the credential, but closing its lease could not be recorded.</summary>
public sealed class RevokedUnrecordedException(Exception closeFailure) : DomainException(FailureMessage, closeFailure)
{
    public const string FailureMessage =
        "The credential was destroyed, but the vault couldn't record it in the audit trail. " +
        "Its lease shows Active until it expires.";
}
