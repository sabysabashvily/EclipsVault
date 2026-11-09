using EclipsVault.Core.Domain.Enums;

namespace EclipsVault.Web.Authentication;

/// <summary>
/// The sign-in a caller proved. Recorded only when that proof is issued a session;
/// a refused issuance records <see cref="AuditAction.SignInRefused"/> with the same details.
/// </summary>
public readonly record struct SignInAudit(AuditAction Action, string? Details);
