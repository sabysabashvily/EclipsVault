using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Domain.Exceptions;

/// <summary>
/// Raised when a prepared Profile operation is refused. Wrong proofs count towards lockout;
/// an incomplete mailbox request may already have been recorded and sent. <see cref="Field"/> names the input an
/// <see cref="ProfileRefusal.InvalidInput"/> refusal is about; the message is safe to show.
/// </summary>
public sealed class ProfileOperationRefusedException(ProfileRefusal refusal, string? field = null, string? message = null)
    : DomainException(message ?? $"The change was refused: {refusal}.")
{
    public ProfileRefusal Refusal { get; } = refusal;

    public string? Field { get; } = field;
}
