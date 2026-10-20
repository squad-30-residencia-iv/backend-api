using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Organization;

namespace VoucherCampaigns.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class EstablishmentsController(IEstablishmentService establishmentService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IEnumerable<EstablishmentResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<EstablishmentResponse>>> GetAll([FromQuery] Guid? regionId, [FromQuery] bool? activeOnly, CancellationToken ct)
    {
        var result = await establishmentService.GetAllAsync(regionId, activeOnly, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<EstablishmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EstablishmentResponse>> GetById(Guid id, CancellationToken ct)
    {
        var result = await establishmentService.GetByIdAsync(id, ct);
        if (result is null) return NotFound(new { error = "NotFound", message = "Estabelecimento não encontrado." });
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType<EstablishmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EstablishmentResponse>> Create([FromBody] CreateEstablishmentRequest request, CancellationToken ct)
    {
        try
        {
            var result = await establishmentService.CreateAsync(request, ct);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { error = "BadRequest", message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<EstablishmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EstablishmentResponse>> Update(Guid id, [FromBody] UpdateEstablishmentRequest request, CancellationToken ct)
    {
        try
        {
            var result = await establishmentService.UpdateAsync(id, request, ct);
            if (result is null) return NotFound(new { error = "NotFound", message = "Estabelecimento não encontrado." });
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
        var success = await establishmentService.InactivateAsync(id, ct);
        if (!success) return NotFound(new { error = "NotFound", message = "Estabelecimento não encontrado." });
        return NoContent();
    }
}
