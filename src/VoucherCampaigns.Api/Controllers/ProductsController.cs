using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Application.Catalog;
using VoucherCampaigns.Application.DTOs.Catalog;

namespace VoucherCampaigns.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class ProductsController(IProductService productService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IEnumerable<ProductResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ProductResponse>>> GetAll([FromQuery] Guid? categoryId, [FromQuery] bool? activeOnly, CancellationToken ct)
    {
        var result = await productService.GetAllAsync(categoryId, activeOnly, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> GetById(Guid id, CancellationToken ct)
    {
        var result = await productService.GetByIdAsync(id, ct);
        if (result is null) return NotFound(new { error = "NotFound", message = "Produto não encontrado." });
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductResponse>> Create([FromBody] CreateProductRequest request, CancellationToken ct)
    {
        try
        {
            var result = await productService.CreateAsync(request, ct);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { error = "BadRequest", message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> Update(Guid id, [FromBody] UpdateProductRequest request, CancellationToken ct)
    {
        try
        {
            var result = await productService.UpdateAsync(id, request, ct);
            if (result is null) return NotFound(new { error = "NotFound", message = "Produto não encontrado." });
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
        var success = await productService.InactivateAsync(id, ct);
        if (!success) return NotFound(new { error = "NotFound", message = "Produto não encontrado." });
        return NoContent();
    }
}
