using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Organization;

namespace VoucherCampaigns.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class RegionsController(IRegionService regionService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IEnumerable<RegionResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<RegionResponse>>> GetAll([FromQuery] Guid? networkId, [FromQuery] bool? activeOnly, CancellationToken ct)
    {
        var result = await regionService.GetAllAsync(networkId, activeOnly, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<RegionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RegionResponse>> GetById(Guid id, CancellationToken ct)
    {
        var result = await regionService.GetByIdAsync(id, ct);
        if (result is null) return NotFound(new { error = "NotFound", message = "Região não encontrada." });
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType<RegionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RegionResponse>> Create([FromBody] CreateRegionRequest request, CancellationToken ct)
    {
        try
        {
            var result = await regionService.CreateAsync(request, ct);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { error = "BadRequest", message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<RegionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RegionResponse>> Update(Guid id, [FromBody] UpdateRegionRequest request, CancellationToken ct)
    {
        try
        {
            var result = await regionService.UpdateAsync(id, request, ct);
            if (result is null) return NotFound(new { error = "NotFound", message = "Região não encontrada." });
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
        var success = await regionService.InactivateAsync(id, ct);
        if (!success) return NotFound(new { error = "NotFound", message = "Região não encontrada." });
        return NoContent();
    }
}
