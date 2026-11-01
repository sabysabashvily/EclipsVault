using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>Raised when approving a request a share could no longer help. The message is safe to show the reviewer.</summary>
public sealed class AccessRequestIneligibleException(AccessRequestRefusal refusal, string message) : DomainException(message)
{
    public AccessRequestRefusal Refusal { get; } = refusal;
}
