namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>Raised when an access request's requester account was deleted while a reviewer approved it.</summary>
public sealed class RequesterNotFoundException : DomainException
{
    public RequesterNotFoundException()
        : base("That requester account no longer exists.")
    {
    }
}
