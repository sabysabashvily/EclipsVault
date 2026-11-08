namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when a range cannot be trusted because it is already trusted, including when another
/// request trusted it first. Nothing was saved.
/// </summary>
public sealed class NetworkExistsException : DomainException
{
    public NetworkExistsException(string? cidr = null) : base(cidr is null ? "That range is already trusted." : $"The range '{cidr}' is already trusted.")
    {
    }
}
