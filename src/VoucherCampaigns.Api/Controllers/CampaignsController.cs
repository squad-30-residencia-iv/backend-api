using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Api.Filters;
using VoucherCampaigns.Application.Campaigns;

namespace VoucherCampaigns.Api.Controllers;

[ApiController]
[Route("api/v1/campaigns")]
public class CampaignsController(ICampaignService campaignService) : ControllerBase
{
    /// <summary>Cadastra a campanha em rascunho (US-01.1). Datas na hora local da rede, sem fuso.</summary>
    [Authorize(Policy = AuthorizationPolicies.ManageCampaigns)]
    [ValidateTenantScope]
    [HttpPost]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CampaignResponse>> Create(CreateCampaignRequest request, CancellationToken cancellationToken)
    {
        var campaign = await campaignService.CreateAsync(request, cancellationToken);

        // A consulta por ID (US-01.9) ainda não existe; o Location já aponta para a rota prevista.
        return Created($"/api/v1/campaigns/{campaign.Id}", campaign);
    }
}
