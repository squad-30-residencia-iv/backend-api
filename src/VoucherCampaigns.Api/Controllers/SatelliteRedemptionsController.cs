using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Application.Vouchers;

namespace VoucherCampaigns.Api.Controllers;

/// <summary>
/// Verificação de aplicáveis, resgate e estorno para as aplicações satélites (EP-05, US-07.3, spec GH-049).
/// A rede é a da aplicação do token.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.SatelliteRedeemVouchers)]
[Route("api/v1/satellite")]
public class SatelliteRedemptionsController(IRedemptionService redemptionService) : ControllerBase
{
    /// <summary>Vouchers do cliente que servem nesta compra, com o desconto previsto e o recomendado; os demais com o motivo. Não grava nada.</summary>
    [HttpPost("vouchers/check")]
    [ProducesResponseType<RedemptionCheckResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RedemptionCheckResponse>> Check(RedemptionCheckRequest request, CancellationToken cancellationToken) =>
        Ok(await redemptionService.CheckAsync(request, cancellationToken));

    /// <summary>Resgata o voucher escolhido: consome uma utilização e grava o histórico. Idempotente por transação.</summary>
    [HttpPost("redemptions")]
    [ProducesResponseType<RedemptionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RedemptionResponse>> Redeem(RedemptionRequest request, CancellationToken cancellationToken) =>
        Ok(await redemptionService.RedeemAsync(request, cancellationToken));

    /// <summary>Estorna o resgate: a utilização volta ao voucher (D-19). Idempotente.</summary>
    [HttpPost("redemptions/{id:guid}/reverse")]
    [ProducesResponseType<RedemptionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RedemptionResponse>> Reverse(Guid id, CancellationToken cancellationToken) =>
        Ok(await redemptionService.ReverseAsync(id, cancellationToken));

    /// <summary>Estado atual do resgate.</summary>
    [HttpGet("redemptions/{id:guid}")]
    [ProducesResponseType<RedemptionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RedemptionResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await redemptionService.GetAsync(id, cancellationToken));
}
