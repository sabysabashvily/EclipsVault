using System.ComponentModel.DataAnnotations;
using EclipsVault.Core.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace EclipsVault.Web.Models.Ui;

/// <summary>
/// Dynamic secrets: the enabled roles the caller could issue now, the leases they may see
/// (<paramref name="LeaseScope"/> "everyone" for an administrator's 50 newest, otherwise "own"), and the
/// licence note. <paramref name="Licence"/> is null when the evaluation window could not be read, so the
/// page leaves the note out.
/// </summary>
public sealed record DynamicSecretsResponse(
    IReadOnlyList<DynamicRoleResponse> Roles,
    [property: AllowedValues("everyone", "own")] string LeaseScope,
    IReadOnlyList<DynamicLeaseResponse> Leases,
    DynamicSecretsLicenceResponse? Licence);

/// <summary>A role's shape, never its statements: those are backend rights in text form.</summary>
public sealed record DynamicRoleResponse(
    Guid RoleId,
    string Name,
    string Description,
    string Project,
    [property: UiEnum(typeof(SecretEnvironment))] string Environment,
    int Sensitivity,
    string SensitivityLabel,
    [property: UiEnum(typeof(DynamicSecretBackend))] string Backend,
    int DefaultTtlMinutes,
    int MaxTtlMinutes);

/// <summary>A lease without credential material. <paramref name="RevocationNote"/> is a fixed sentence per cause.</summary>
public sealed record DynamicLeaseResponse(
    Guid LeaseId,
    string RoleName,
    string CredentialIdentity,
    string Holder,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ClosedAtUtc,
    [property: UiEnum(typeof(LeaseStatus))] string Status,
    string? RevocationNote);

/// <summary>
/// Only meaningful while <paramref name="Licensed"/> is false: a null <paramref name="EvaluationDaysRemaining"/>
/// means the evaluation has not started, a positive number that it is running, and zero that it is spent.
/// </summary>
public sealed record DynamicSecretsLicenceResponse(bool Licensed, int? EvaluationDaysRemaining);

/// <summary>Issue a credential. A null lease takes the role's default; the action checks the range itself.</summary>
[ValidateNever]
public sealed class IssueDynamicCredentialRequest
{
    public Guid RoleId { get; init; }

    public int? TtlMinutes { get; init; }
}

/// <summary>The credential, in this response only: the vault keeps no copy of <paramref name="Password"/>.</summary>
public sealed record IssuedDynamicCredentialResponse(Guid LeaseId, string RoleName, string Username, string Password, DateTimeOffset ExpiresAtUtc);

/// <summary>How the lease closed, with the fixed sentence that says why when it was not destroyed.</summary>
public sealed record RevokedLeaseResponse(
    [property: UiEnum(typeof(LeaseStatus))] string Status,
    string? RevocationNote);
