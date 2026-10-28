using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Core.Application.Profile;

/// <summary>
/// The intended change, as the owner asked for it. Only the fields of its <see cref="Kind"/> may be
/// set: <see cref="NewPassword"/> for a password change, <see cref="PasskeyNickname"/> for adding a
/// passkey, <see cref="PasskeyId"/> for removing one.
/// </summary>
public sealed record ProfileChangeRequest(ProfileOperationKind Kind, string? NewPassword, string? PasskeyNickname, Guid? PasskeyId);

/// <summary>A prepared change and the methods that can verify it.</summary>
public sealed record PreparedOperation(
    Guid OperationId, ProfileOperationKind Kind, DateTimeOffset ExpiresAtUtc, IReadOnlyList<AccountProofMethod> Methods);

/// <summary>A verified change, which its final action must commit by <see cref="CommitByUtc"/>.</summary>
public sealed record VerifiedOperation(Guid OperationId, DateTimeOffset CommitByUtc);

/// <summary>What the owner can change on the Profile page, and why the rest is unavailable.</summary>
public sealed record ProfileSecurity(
    IReadOnlyList<AccountProofMethod> Methods,
    IReadOnlyDictionary<ProfileOperationKind, ProfileRefusal?> Changes,
    IReadOnlyDictionary<Guid, ProfileRefusal?> PasskeyRemovals);

/// <summary>A committed passkey registration and the credential generation it committed.</summary>
public sealed record RegisteredPasskey(PasskeySummary Passkey, long CredentialGeneration);
