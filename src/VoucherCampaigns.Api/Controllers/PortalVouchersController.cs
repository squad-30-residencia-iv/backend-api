using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Api.Filters;
using VoucherCampaigns.Application.Vouchers;

namespace VoucherCampaigns.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.ManageCampaigns)]
[Route("api/v1/vouchers")]
public class PortalVouchersController(IPortalVoucherQueryService vouchers) : ControllerBase
{
    /// <summary>Lista vouchers da rede para o portal (GH-050).</summary>
    [HttpGet]
    [ValidateTenantScope]
    [ProducesResponseType<IReadOnlyList<PortalVoucherListItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PortalVoucherListItemResponse>>> GetAll(
        [FromQuery, Required(ErrorMessage = PortalVoucherContract.NetworkRequired)] Guid? networkId,
        [FromQuery] Guid? campaignId,
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] DateTime? issuedFrom,
        [FromQuery] DateTime? issuedTo,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        // Paginação opcional (spec GH-071, D-5): o corpo continua uma lista; o total vai no cabeçalho.
        var result = await vouchers.ListAsync(networkId, campaignId, status, search, issuedFrom, issuedTo, page, pageSize, cancellationToken);
        Response.Headers[PagingHeaders.TotalCount] = result.Total.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return Ok(result.Items);
    }

    /// <summary>Detalhe e histórico do voucher no portal (GH-050).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<PortalVoucherDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortalVoucherDetailResponse>> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await vouchers.GetByIdAsync(id, cancellationToken));
}
