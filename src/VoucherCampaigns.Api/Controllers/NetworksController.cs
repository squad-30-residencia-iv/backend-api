using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Organization;

namespace VoucherCampaigns.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class NetworksController(INetworkService networkService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IEnumerable<NetworkResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<NetworkResponse>>> GetAll([FromQuery] bool? activeOnly, CancellationToken ct)
    {
        var result = await networkService.GetAllAsync(activeOnly, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<NetworkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NetworkResponse>> GetById(Guid id, CancellationToken ct)
    {
        var result = await networkService.GetByIdAsync(id, ct);
        if (result is null) return NotFound(new { error = "NotFound", message = "Rede não encontrada." });
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType<NetworkResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NetworkResponse>> Create([FromBody] CreateNetworkRequest request, CancellationToken ct)
    {
        try
        {
            var result = await networkService.CreateAsync(request, ct);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { error = "BadRequest", message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<NetworkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NetworkResponse>> Update(Guid id, [FromBody] UpdateNetworkRequest request, CancellationToken ct)
    {
        try
        {
            var result = await networkService.UpdateAsync(id, request, ct);
            if (result is null) return NotFound(new { error = "NotFound", message = "Rede não encontrada." });
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
        var success = await networkService.InactivateAsync(id, ct);
        if (!success) return NotFound(new { error = "NotFound", message = "Rede não encontrada." });
        return NoContent();
    }
}
