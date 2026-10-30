using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers;

/// <summary>
/// Base class for the interactive, cookie-authenticated MVC controllers. It exposes the signed-in
/// principal's identity through small, fail-closed accessors so no controller re-implements claim
/// parsing — the logic lives once in <see cref="ClaimsPrincipalExtensions"/>.
///
/// <para>
/// The <c>[Authorize(Policy = VaultPolicies.Session)]</c> here is deliberately redundant with the default-deny fallback policy in
/// <c>Program.cs</c>, and both should stay. The fallback is a single line in startup that protects
/// every controller carrying no attribute of its own; if it were ever edited away, nothing in the
/// controllers themselves would object and the vault would serve secrets to anonymous callers. A
/// vault should not have one line standing between an unauthenticated request and its contents, so
/// the requirement is also written where the code being protected lives.
/// </para>
/// </summary>
[Authorize(Policy = VaultPolicies.Session)]
public abstract class VaultController : Controller
{
    /// <summary>The signed-in user's id, or <see cref="Guid.Empty"/> when absent (fail-closed).</summary>
    protected Guid CurrentUserId() => User.GetUserId();

    /// <summary>The signed-in user's immutable login username (the audit anchor), or empty.</summary>
    protected string CurrentUsername() => User.GetUsername();

    /// <summary>This device's session id, or null when the principal carries none.</summary>
    protected Guid? CurrentSessionId() => User.GetSessionId();

    /// <summary>The credential generation this session was issued under; a session without one is stale.</summary>
    protected long CurrentCredentialGeneration()
        => User.GetSessionStampOrNull()?.CredentialGeneration ?? throw new StaleProofException();
}
