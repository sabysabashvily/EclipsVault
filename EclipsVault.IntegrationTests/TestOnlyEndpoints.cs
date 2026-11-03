using EclipsVault.Core.Application.Abstractions;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Controllers.Ui;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.IntegrationTests;

/// <summary>
/// The failures the error mapping must handle, raised on demand (ADR-0006). The endpoints that raise
/// them live in this assembly, which the vault never loads: only <see cref="VaultFixture"/> adds them
/// to the host under test, so the production application cannot expose them.
/// </summary>
internal static class TestOnlyFailures
{
    /// <summary>Carried in every exception message. No response may repeat it.</summary>
    public const string Detail = "it-exception-detail-must-not-reach-the-body";

    /// <summary>A role no session holds, so an endpoint requiring it is refused to everyone.</summary>
    public const string UnheldRole = "it-role-no-session-holds";

    public static async Task ThrowAsync(string kind, IIntrusionResponseService intrusion, CancellationToken ct)
    {
        switch (kind)
        {
            case "honey_token":
                // What SecretService does when a decoy is opened: the intrusion response revokes the
                // account's sessions first, then the exception reaches the middleware.
                var decoy = Guid.NewGuid();
                await intrusion.TriggerHoneyTokenAsync(decoy, Detail, ct);
                throw new HoneyTokenTrippedException(decoy, Detail);
            case "audit":
                throw new AuditWriteFailedException(Detail, new InvalidOperationException(Detail));
            case "not_found":
                throw new SecretNotFoundException(Guid.NewGuid());
            case "legacy_blob":
                throw new LegacyBlobRefusedException(Detail);
            case "crypto":
                throw new CryptoConfigurationException(Detail);
            case "premium":
                throw new PremiumFeatureNotLicensedException(Detail);
            case "domain":
                // Any domain exception without a mapping of its own.
                throw new VaultAdminException(Detail);
            default:
                throw new InvalidOperationException(Detail);
        }
    }
}

[ApiController]
[Route("api/ui/test-only")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public sealed class TestOnlyUiApiController(IIntrusionResponseService intrusion) : ControllerBase
{
    [HttpGet("throw/{kind}")]
    public Task Throw(string kind, CancellationToken ct) => TestOnlyFailures.ThrowAsync(kind, intrusion, ct);

    [HttpGet("forbidden")]
    [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Roles = TestOnlyFailures.UnheldRole)]
    public IActionResult Forbidden() => NoContent();

    // These conflicts need concurrent requests in production. The actual races are covered against
    // both databases; this endpoint pins their HTTP mapping without a scheduler-dependent request race.
    [HttpGet("administrator-conflict/{kind}")]
    [TypeFilter(typeof(AdministratorProblemFilter))]
    public IActionResult AdministratorConflict(string kind) => kind switch
    {
        "last-administrator" => throw new LastAdministratorException("Cannot disable the last TopSecret administrator."),
        _ => throw new AccountChangedException()
    };

    /// <summary>An unsafe UI API request that changes nothing, so the antiforgery check alone decides its answer.</summary>
    [HttpPost("unsafe")]
    public IActionResult Unsafe() => NoContent();

    /// <summary>
    /// What approval throws when another request saved the requester's share first. Only a race produces it,
    /// so this raises it under the filter the access-request endpoints use.
    /// </summary>
    [HttpGet("access-request-share-exists")]
    [TypeFilter(typeof(AccessRequestProblemFilter))]
    public IActionResult AccessRequestShareExists() => throw new SharingException(TestOnlyFailures.Detail);

    /// <summary>
    /// What approval throws when the requester's account is deleted between reading the request and looking up
    /// its requester. Deleting the account deletes its requests too, so only that race produces it.
    /// </summary>
    [HttpGet("access-request-requester-gone")]
    [TypeFilter(typeof(AccessRequestProblemFilter))]
    public IActionResult AccessRequestRequesterGone() => throw new RequesterNotFoundException();
}

/// <summary>The same failures on a Legacy UI path, which must keep its redirects.</summary>
[Route("test-only")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public sealed class TestOnlyLegacyController(IIntrusionResponseService intrusion) : Controller
{
    [HttpGet("throw/{kind}")]
    public Task Throw(string kind, CancellationToken ct) => TestOnlyFailures.ThrowAsync(kind, intrusion, ct);

    [HttpGet("forbidden")]
    [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Roles = TestOnlyFailures.UnheldRole)]
    public IActionResult Forbidden() => NoContent();
}
