using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Organization;

namespace VoucherCampaigns.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class ChannelsController(IChannelService channelService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IEnumerable<ChannelResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ChannelResponse>>> GetAll([FromQuery] Guid? establishmentId, [FromQuery] bool? activeOnly, CancellationToken ct)
    {
        var result = await channelService.GetAllAsync(establishmentId, activeOnly, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ChannelResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChannelResponse>> GetById(Guid id, CancellationToken ct)
    {
        var result = await channelService.GetByIdAsync(id, ct);
        if (result is null) return NotFound(new { error = "NotFound", message = "Canal não encontrado." });
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType<ChannelResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChannelResponse>> Create([FromBody] CreateChannelRequest request, CancellationToken ct)
    {
        try
        {
            var result = await channelService.CreateAsync(request, ct);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { error = "BadRequest", message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ChannelResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChannelResponse>> Update(Guid id, [FromBody] UpdateChannelRequest request, CancellationToken ct)
    {
        try
        {
            var result = await channelService.UpdateAsync(id, request, ct);
            if (result is null) return NotFound(new { error = "NotFound", message = "Canal não encontrado." });
            return Ok(result);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { error = "BadRequest", message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var success = await channelService.InactivateAsync(id, ct);
        if (!success) return NotFound(new { error = "NotFound", message = "Canal não encontrado." });
        return NoContent();
    }
}
