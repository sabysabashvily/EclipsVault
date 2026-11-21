namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// The backend refused a managed rotation's new password, so nothing moved: the login and the stored
/// value still agree. The message is safe to show; the backend's own error is the inner exception.
/// </summary>
public sealed class UpstreamRotationRefusedException : DomainException
{
    public UpstreamRotationRefusedException(Exception refusal)
        : base("The backend refused the rotation. The credential is unchanged.", refusal)
    {
    }
}
