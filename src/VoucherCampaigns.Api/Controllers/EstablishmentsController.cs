using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Organization;

namespace VoucherCampaigns.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.ReadHierarchy)]
[Route("api/v1/establishments")]
public class EstablishmentsController(IEstablishmentService establishmentService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<EstablishmentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<EstablishmentResponse>>> GetAll(
        [FromQuery, Required(ErrorMessage = OrganizationContract.RegionRequired)] Guid? regionId,
        [FromQuery] bool? activeOnly,
        CancellationToken cancellationToken) =>
        Ok(await establishmentService.GetAllAsync(regionId, activeOnly, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<EstablishmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EstablishmentResponse>> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await establishmentService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.ManageHierarchy)]
    [ProducesResponseType<EstablishmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EstablishmentResponse>> Create([FromBody] CreateEstablishmentRequest request, CancellationToken cancellationToken)
    {
        var result = await establishmentService.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/establishments/{result.Id}", result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManageHierarchy)]
    [ProducesResponseType<EstablishmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EstablishmentResponse>> Update(
        Guid id, [FromBody] UpdateEstablishmentRequest request, CancellationToken cancellationToken) =>
        Ok(await establishmentService.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManageHierarchy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await establishmentService.InactivateAsync(id, cancellationToken);
        return NoContent();
    }
}
