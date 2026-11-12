using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Application.Campaigns;

namespace VoucherCampaigns.Api.Controllers;

/// <summary>Campanhas vigentes para as aplicações satélites (US-07.1, spec GH-049 D-14). A rede é a da aplicação do token.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.SatelliteReadCampaigns)]
[Route("api/v1/satellite")]
public class SatelliteCampaignsController(ISatelliteCampaignService campaignService) : ControllerBase
{
    /// <summary>Campanhas efetivamente ativas, sem saldo; filtradas pela loja e pelo canal quando informados.</summary>
    [HttpGet("campaigns")]
    [ProducesResponseType<IReadOnlyList<SatelliteCampaignResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<SatelliteCampaignResponse>>> GetActive(
        [FromQuery] Guid? establishmentId, [FromQuery] string? channel, CancellationToken cancellationToken) =>
        Ok(await campaignService.GetActiveAsync(establishmentId, channel, cancellationToken));
}
