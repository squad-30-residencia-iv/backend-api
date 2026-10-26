using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Api.Filters;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Organization;

namespace VoucherCampaigns.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.ReadHierarchy)]
[Route("api/v1/networks")]
public class NetworksController(INetworkService networkService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<NetworkResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<NetworkResponse>>> GetAll([FromQuery] bool? activeOnly, CancellationToken cancellationToken) =>
        Ok(await networkService.GetAllAsync(activeOnly, cancellationToken));

    [HttpGet("{networkId:guid}")]
    [ValidateTenantScope]
    [ProducesResponseType<NetworkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NetworkResponse>> GetById(Guid networkId, CancellationToken cancellationToken) =>
        Ok(await networkService.GetByIdAsync(networkId, cancellationToken));

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.ManageHierarchy)]
    [ProducesResponseType<NetworkResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<NetworkResponse>> Create([FromBody] CreateNetworkRequest request, CancellationToken cancellationToken)
    {
        var result = await networkService.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/networks/{result.Id}", result);
    }

    [HttpPut("{networkId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManageHierarchy)]
    [ProducesResponseType<NetworkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<NetworkResponse>> Update(
        Guid networkId, [FromBody] UpdateNetworkRequest request, CancellationToken cancellationToken) =>
        Ok(await networkService.UpdateAsync(networkId, request, cancellationToken));

    [HttpDelete("{networkId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManageHierarchy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid networkId, CancellationToken cancellationToken)
    {
        await networkService.InactivateAsync(networkId, cancellationToken);
        return NoContent();
    }
}
