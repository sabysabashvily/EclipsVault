namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>Raised when a master key rotation is asked for while another is running. Nothing was changed.</summary>
public sealed class KekRotationRunningException : DomainException
{
    public KekRotationRunningException() : base("Another master key rotation is already running.")
    {
    }
}
