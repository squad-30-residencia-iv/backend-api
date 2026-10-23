using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Organization;

namespace VoucherCampaigns.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.ManageCampaigns)]
[Route("api/v1/channels")]
public class ChannelsController(IChannelService channelService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ChannelResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ChannelResponse>>> GetAll(
        [FromQuery, Required(ErrorMessage = OrganizationContract.EstablishmentRequired)] Guid? establishmentId,
        [FromQuery] bool? activeOnly,
        CancellationToken cancellationToken) =>
        Ok(await channelService.GetAllAsync(establishmentId, activeOnly, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ChannelResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChannelResponse>> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await channelService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [ProducesResponseType<ChannelResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ChannelResponse>> Create([FromBody] CreateChannelRequest request, CancellationToken cancellationToken)
    {
        var result = await channelService.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/channels/{result.Id}", result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ChannelResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ChannelResponse>> Update(Guid id, [FromBody] UpdateChannelRequest request, CancellationToken cancellationToken) =>
        Ok(await channelService.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await channelService.InactivateAsync(id, cancellationToken);
        return NoContent();
    }
}
