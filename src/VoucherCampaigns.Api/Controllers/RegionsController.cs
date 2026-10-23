using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Api.Filters;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Organization;

namespace VoucherCampaigns.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.ManageCampaigns)]
[Route("api/v1/regions")]
public class RegionsController(IRegionService regionService) : ControllerBase
{
    [HttpGet]
    [ValidateTenantScope]
    [ProducesResponseType<IReadOnlyList<RegionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<RegionResponse>>> GetAll(
        [FromQuery, Required(ErrorMessage = OrganizationContract.NetworkRequired)] Guid? networkId,
        [FromQuery] bool? activeOnly,
        CancellationToken cancellationToken) =>
        Ok(await regionService.GetAllAsync(networkId, activeOnly, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<RegionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RegionResponse>> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await regionService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [ValidateTenantScope]
    [ProducesResponseType<RegionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RegionResponse>> Create([FromBody] CreateRegionRequest request, CancellationToken cancellationToken)
    {
        var result = await regionService.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/regions/{result.Id}", result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<RegionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RegionResponse>> Update(Guid id, [FromBody] UpdateRegionRequest request, CancellationToken cancellationToken) =>
        Ok(await regionService.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await regionService.InactivateAsync(id, cancellationToken);
        return NoContent();
    }
}
