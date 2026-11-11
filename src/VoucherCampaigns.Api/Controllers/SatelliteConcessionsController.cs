using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Application.Vouchers;

namespace VoucherCampaigns.Api.Controllers;

/// <summary>
/// Motor de concessão para as aplicações satélites (EP-04, US-07.2, US-07.4). A rede é a da aplicação do token;
/// nada é informado pelo satélite. Separada da <see cref="SatelliteController"/> para não conflitar com a administração (#48).
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.SatelliteIssueVouchers)]
[Route("api/v1/satellite")]
public class SatelliteConcessionsController(IConcessionService concessionService) : ControllerBase
{
    /// <summary>Avalia a compra com o carrinho fechado e concede, no máximo, um voucher provisório. Não haver concessão não é erro.</summary>
    [HttpPost("concessions")]
    [ProducesResponseType<ConcessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ConcessionResponse>> Grant(ConcessionRequest request, CancellationToken cancellationToken) =>
        Ok(await concessionService.GrantAsync(request, cancellationToken));

    /// <summary>Confirma a venda: o voucher passa a disponível e o saldo é consumido (US-04.9).</summary>
    [HttpPost("concessions/{id:guid}/confirm")]
    [ProducesResponseType<ConcessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ConcessionResponse>> Confirm(Guid id, CancellationToken cancellationToken) =>
        Ok(await concessionService.ConfirmAsync(id, cancellationToken));

    /// <summary>Desfaz a venda: o voucher é descartado e o saldo devolvido (US-04.9).</summary>
    [HttpPost("concessions/{id:guid}/cancel")]
    [ProducesResponseType<ConcessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ConcessionResponse>> Cancel(Guid id, CancellationToken cancellationToken) =>
        Ok(await concessionService.CancelAsync(id, cancellationToken));

    /// <summary>Estado atual da concessão.</summary>
    [HttpGet("concessions/{id:guid}")]
    [ProducesResponseType<ConcessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConcessionResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await concessionService.GetAsync(id, cancellationToken));

    /// <summary>Vouchers do cliente na rede da aplicação, com o status efetivo (US-04.8, US-07.4).</summary>
    [HttpGet("customers/{document}/vouchers")]
    [ProducesResponseType<IReadOnlyList<VoucherResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<VoucherResponse>>> CustomerVouchers(
        string document, [FromQuery] string? status, CancellationToken cancellationToken) =>
        Ok(await concessionService.GetCustomerVouchersAsync(document, status, cancellationToken));
}
