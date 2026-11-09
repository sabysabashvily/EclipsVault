using EclipsVault.Web.Authorization;
using EclipsVault.Web.Extensions;
using EclipsVault.Web.Models.Ui;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EclipsVault.Web.Controllers.Ui;

[ApiController]
[Route("api/ui/networks")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme, Policy = VaultPolicies.AdminOnly)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[TypeFilter(typeof(AdministratorProblemFilter))]
public sealed class NetworksController(ITrustedNetworkService trustedNetworks, IIpBlacklist blacklist, IBlockAdministration blocks,
    AdministratorStepUp stepUp, IOptions<AbacOptions> abac, ILogger<NetworksController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<NetworksResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<NetworksResponse>> Get(CancellationToken ct)
    {
        var source = HttpContext.Connection.RemoteIpAddress;
        var configured = abac.Value.TrustedIpCidrs;
        var trusted = NetworkRules.IsInAnyCidr(source, configured)
                      || (source is not null && await trustedNetworks.IsTrustedAsync(source, ct));
        return new NetworksResponse(source is null ? "unknown" : NetworkRules.Normalize(source).ToString(), trusted, configured,
            (await trustedNetworks.ListAsync(ct)).Select(network => new TrustedNetworkResponse(
                network.Id, network.Cidr, network.Label, network.AddedBy, network.CreatedAtUtc)).ToList(),
            (await blacklist.ListAsync(ct)).Select(block => new NetworkBlockResponse(block.Network, block.Reason, block.BlockedAtUtc)).ToList());
    }

    [HttpPost("trust-current-address")]
    [ProducesResponseType<TrustedRangeResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> TrustCurrentAddress(CancellationToken ct)
    {
        if (await stepUp.RequireAsync(User, AdministratorAction.TrustNetwork, ct) is { } required) return required;
        if (HttpContext.Connection.RemoteIpAddress is not { } source)
        {
            return AdministratorProblems.Problem(StatusCodes.Status400BadRequest, "validation_failed",
                "The vault could not determine your source address.");
        }

        var added = await trustedNetworks.AddAsync(NetworkRules.Normalize(source).ToString(),
            $"Trusted from admin console by {User.GetUsername()}", ct);
        return StatusCode(StatusCodes.Status201Created, new TrustedRangeResponse(added.Cidr));
    }

    [HttpPost]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<TrustedRangeResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Add([FromBody] AddNetworkRequest request, CancellationToken ct)
    {
        if (await stepUp.RequireAsync(User, AdministratorAction.TrustNetwork, ct) is { } required) return required;
        var added = await trustedNetworks.AddAsync(request.Range ?? "", request.Label ?? "", ct);
        return StatusCode(StatusCodes.Status201Created, new TrustedRangeResponse(added.Cidr));
    }

    [HttpDelete("{networkId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Remove(Guid networkId, CancellationToken ct)
        => await trustedNetworks.RemoveAsync(networkId, ct) ? NoContent()
            : AdministratorProblems.Problem(StatusCodes.Status404NotFound, "network_not_found", "Network entry not found.");

    [HttpPost("blocks/lift")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<AdministratorProblemResponse>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> LiftBlock([FromBody] LiftBlockRequest request, CancellationToken ct)
    {
        if (await stepUp.RequireAsync(User, AdministratorAction.LiftBlock, ct) is { } required) return required;
        var range = request.Range ?? "";
        if (!await blocks.LiftAsync(range, ct))
        {
            return AdministratorProblems.Problem(StatusCodes.Status404NotFound, "block_not_found", "That range is not currently blocked.");
        }

        logger.LogWarning("Administrator {Username} lifted the intrusion-defence block on {Network}", User.GetUsername(), range);
        return NoContent();
    }
}
