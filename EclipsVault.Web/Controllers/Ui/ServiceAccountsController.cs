using EclipsVault.Core.Domain.Enums;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models.Ui;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EclipsVault.Web.Controllers.Ui;

[ApiController]
[Route("api/ui/service-accounts")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.AdminOnly)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[TypeFilter(typeof(AdministratorProblemFilter))]
public sealed class ServiceAccountsController(IServiceAccountService accounts, AdministratorStepUp stepUp) : ControllerBase
{
    private static IReadOnlyList<ClearanceOptionResponse> Clearances => Enum.GetValues<ClearanceLevel>()
        .Select(level => new ClearanceOptionResponse((int)level, level.ToDisplayName())).ToList();

    private static JsonResult AccountNotFound() => AdministratorProblems.Problem(StatusCodes.Status404NotFound,
        "service_account_not_found", "Service account not found.");

    [HttpGet]
    [ProducesResponseType<ServiceAccountsResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<ServiceAccountsResponse>> Get(CancellationToken ct)
        => new ServiceAccountsResponse((await accounts.ListAsync(ct)).Select(account => new ServiceAccountResponse(
            account.Id, account.Name, (int)account.Clearance, account.Clearance.ToDisplayName(), account.ProjectKey,
            account.ActiveKeyCount, !account.IsDisabled, account.CreatedAtUtc)).ToList());

    [HttpGet("new")]
    [ProducesResponseType<NewServiceAccountResponse>(StatusCodes.Status200OK, "application/json")]
    public ActionResult<NewServiceAccountResponse> New() => new NewServiceAccountResponse(Clearances);

    [HttpPost]
    [RequestSizeLimit(8192)]
    [ProducesResponseType<CreatedServiceAccountResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Create([FromBody] CreateServiceAccountUiRequest request, CancellationToken ct)
    {
        var id = await accounts.CreateAsync(new CreateServiceAccountRequest(request.Name ?? "",
            (ClearanceLevel)(request.Clearance ?? 0), request.Project ?? ""), ct);
        return Created($"/api/ui/service-accounts/{id}", new CreatedServiceAccountResponse(id));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ServiceAccountDetailsResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ServiceAccountDetailsResponse>> Details(Guid id, CancellationToken ct)
    {
        var account = await accounts.GetAsync(id, ct);
        if (account is null) return AccountNotFound();
        return new ServiceAccountDetailsResponse(account.Id, account.Name, (int)account.Clearance,
            account.Clearance.ToDisplayName(), account.ProjectKey, !account.IsDisabled, account.CreatedAtUtc,
            Clearances.Where(level => level.Value < (int)account.Clearance).ToList(),
            account.Keys.Select(key => new ServiceAccountKeyResponse(key.Id, key.Prefix, key.CreatedAtUtc,
                key.ExpiresAtUtc, key.LastUsedAtUtc, KeyStatus(key.Status), !key.IsScoped, key.MetadataOnly,
                key.ClearanceCeiling?.ToDisplayName(), key.ProjectScope, key.AllowedCidrs)).ToList());
    }

    private static string KeyStatus(ApiKeyStatus status) => status switch
    {
        ApiKeyStatus.Active => "active",
        ApiKeyStatus.Expired => "expired",
        ApiKeyStatus.Revoked => "revoked",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    [HttpPost("{id:guid}/keys")]
    [RequestSizeLimit(8192)]
    [ProducesResponseType<IssuedServiceAccountKeyResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> IssueKey(Guid id, [FromBody] IssueServiceAccountKeyRequest request, CancellationToken ct)
    {
        if (await stepUp.RequireAsync(User, AdministratorAction.IssueApiKey, ct) is { } required) return required;
        var key = await accounts.IssueKeyAsync(id, new IssueApiKeyRequest(request.ExpiresInDays!.Value,
            request.ClearanceCeiling is { } ceiling ? (ClearanceLevel)ceiling : null,
            request.ProjectScope, request.MetadataOnly, request.AllowedRanges), ct);
        return key is null ? AccountNotFound()
            : Created($"/api/ui/service-accounts/{id}/keys/{key.Id}", new IssuedServiceAccountKeyResponse(key.Id, key.Prefix, key.RawToken));
    }

    [HttpDelete("{id:guid}/keys/{keyId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> RevokeKey(Guid id, Guid keyId, CancellationToken ct)
        => await accounts.RevokeKeyAsync(id, keyId, ct) ? NoContent()
            : AdministratorProblems.Problem(StatusCodes.Status404NotFound, "key_not_found", "Key not found.");

    [HttpPost("{id:guid}/enable")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Enable(Guid id, CancellationToken ct)
    {
        if (await stepUp.RequireAsync(User, AdministratorAction.EnableServiceAccount, ct) is { } required) return required;
        return await accounts.SetEnabledAsync(id, true, ct) ? NoContent() : AccountNotFound();
    }

    [HttpPost("{id:guid}/disable")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
        => await accounts.SetEnabledAsync(id, false, ct) ? NoContent() : AccountNotFound();

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => await accounts.DeleteAsync(id, ct) ? NoContent() : AccountNotFound();
}
