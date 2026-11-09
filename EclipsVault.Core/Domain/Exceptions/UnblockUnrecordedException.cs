namespace EclipsVault.Core.Domain.Exceptions;

public sealed class UnblockUnrecordedException() : DomainException(FailureMessage)
{
    public const string FailureMessage = "The block was lifted, but recording and restoring it failed. Check the range before retrying.";
}
