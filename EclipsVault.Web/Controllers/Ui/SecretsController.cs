using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using EclipsVault.Core.Application.Abac;
using EclipsVault.Core.Application.Secrets;
using EclipsVault.Core.Domain.Enums;
using EclipsVault.Core.Domain.Exceptions;
using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Middleware;
using EclipsVault.Web.Models.Ui;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace EclipsVault.Web.Controllers.Ui;

/// <summary>
/// The Secrets list, the palettes' search and New secret. Reads write no audit row. A name is not
/// nothing ("Production_AWS_Root_Key" says what exists and what it is worth), so the list and the
/// search show only rows the caller could open now, through the service's list (which drops decoys)
/// and the same access handler that gates opening one.
/// </summary>
[ApiController]
[Route("api/ui/secrets")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.Session)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SecretsController(
    ISecretService secrets,
    IAuthorizationService authorization,
    ITempDataDictionaryFactory tempData,
    ILogger<SecretsController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SecretsResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<SecretsResponse>> Get(CancellationToken ct)
        => new SecretsResponse((await ListableAsync(ct)).Select(secret => new SecretListItemResponse(
            secret.Id, secret.Name, secret.ProjectKey, secret.Environment.ToString(), (int)secret.Sensitivity,
            secret.Sensitivity.ToDisplayName(), secret.CreatedAtUtc, secret.ExpiresAtUtc)).ToList());

    [HttpGet("search")]
    public async Task<ActionResult<SecretSearchResponse>> Search(string? q, CancellationToken ct)
        => new SecretSearchResponse(SecretSearch.Find(await ListableAsync(ct), q).Select(secret => new SecretSearchResultResponse(
            secret.Id, secret.Name, secret.ProjectKey, secret.Environment.ToString(), (int)secret.Sensitivity,
            secret.Sensitivity.ToDisplayName(), $"/Secrets/Details/{secret.Id}")));

    [HttpPost]
    [ProducesResponseType<CreatedSecretResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<SecretProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Create([FromBody] CreateSecretUiRequest request, CancellationToken ct)
    {
        var errors = FieldErrors(request);
        if (errors.Count > 0)
        {
            return Refusal(StatusCodes.Status400BadRequest, "validation_failed", "Check the highlighted fields.", errors: errors);
        }

        var environment = Enum.Parse<SecretEnvironment>(request.Environment!);
        var sensitivity = (SensitivityLevel)request.Sensitivity!.Value;

        // A session missing the vault attribute claims is stale: refuse it rather than judge it by a
        // clearance or project the server never issued.
        if (User.GetClearanceOrNull() is not { } clearance || User.GetProject().Length == 0)
        {
            return UiApiProblem.StaleSession();
        }

        // Project and sensitivity come from the form, so creation enforces the same boundary as reading:
        // a caller cannot plant a credential in another project.
        var project = request.Project!.Trim();
        var decision = SecretCreationPolicy.Evaluate(new SubjectAttributes(clearance, User.GetProject()), project, sensitivity);
        if (!decision.IsAllowed)
        {
            logger.LogWarning("User {Username} was denied creation of a secret in project {ProjectKey}: {Reasons}",
                User.Identity?.Name, project, string.Join(" ", decision.DenialReasons));
            return Refusal(StatusCodes.Status403Forbidden, "creation_denied", "The vault refused to create this secret.", decision.DenialReasons);
        }

        Guid id;
        try
        {
            id = await secrets.CreateAsync(
                new CreateSecretRequest(request.Name!, request.Value!, project, environment, sensitivity, request.TtlDays!.Value), ct);
        }
        catch (SecretNameTakenException taken)
        {
            return Refusal(StatusCodes.Status409Conflict, "name_taken", taken.Message);
        }

        // The Legacy Details page shows this until it is served by the Angular UI.
        var flash = tempData.GetTempData(HttpContext);
        flash[FlashExtensions.TypeKey] = "success";
        flash[FlashExtensions.MessageKey] = $"Secret '{request.Name}' was envelope-encrypted and stored.";
        return Created($"/Secrets/Details/{id}", new CreatedSecretResponse(id));
    }

    private async Task<List<SecretSummaryDto>> ListableAsync(CancellationToken ct)
        => await authorization.VisibleToAsync(User, await secrets.ListAsync(ct));

    /// <summary>
    /// The Legacy form's messages for its own fields. The environment and sensitivity come from lists the
    /// form offers, so a value outside them gets no Legacy message of its own.
    /// </summary>
    private static Dictionary<string, string[]> FieldErrors(CreateSecretUiRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        var errors = results
            .GroupBy(result => JsonNamingPolicy.CamelCase.ConvertName(result.MemberNames.Single()))
            .ToDictionary(field => field.Key, field => field.Select(result => result.ErrorMessage!).ToArray());
        if (request.TtlDays is null)
        {
            errors["ttlDays"] = ["The value '' is invalid."];
        }

        if (!Enum.GetNames<SecretEnvironment>().Contains(request.Environment))
        {
            errors["environment"] = ["This field is missing or invalid."];
        }

        if (request.Sensitivity is not { } sensitivity || !Enum.IsDefined((SensitivityLevel)sensitivity))
        {
            errors["sensitivity"] = ["This field is missing or invalid."];
        }

        return errors;
    }

    private static JsonResult Refusal(int status, string code, string title, IReadOnlyList<string>? reasons = null,
        IReadOnlyDictionary<string, string[]>? errors = null)
        => new(new SecretProblemResponse { Status = status, Code = code, Title = title, Reasons = reasons, Errors = errors })
        {
            StatusCode = status,
            ContentType = "application/problem+json"
        };
}
