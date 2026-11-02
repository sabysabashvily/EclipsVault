namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when a new service account cannot be saved because another one took its name first.
/// Nothing was saved.
/// </summary>
public sealed class ServiceAccountNameTakenException : DomainException
{
    public ServiceAccountNameTakenException() : base("That service account name is already taken.")
    {
    }
}
