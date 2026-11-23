using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// The master keys in use. <paramref name="AwaitingRotation"/> counts the secrets and versions a
/// rotation can move; <paramref name="OnMissingKeys"/> counts those under master keys that are not loaded.
/// </summary>
public sealed record EncryptionResponse(string CurrentKekId, IReadOnlyList<string> LoadedKekIds, IReadOnlyList<KekResponse> Keys,
    int AwaitingRotation, int OnMissingKeys);

public sealed record KekResponse(string KekId,
    [property: AllowedValues("current", "retired", "not_loaded")] string Status, int Secrets, int Versions);

public sealed record RotationResponse(string CurrentKekId, int SecretsRewrapped, int VersionsRewrapped, int OnMissingKeys);

/// <summary>A rotation that stopped at a batch it could not record. The counts are what the batches committed before it moved.</summary>
public sealed class RotationStoppedProblemResponse : ProblemDetails
{
    public required string Code { get; init; }

    public required int SecretsRewrapped { get; init; }

    public required int VersionsRewrapped { get; init; }
}
