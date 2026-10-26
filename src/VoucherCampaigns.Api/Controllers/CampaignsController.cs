using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Api.Filters;
using VoucherCampaigns.Application.Campaigns;

namespace VoucherCampaigns.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.ManageCampaigns)]
[Route("api/v1/campaigns")]
public class CampaignsController(ICampaignService campaignService) : ControllerBase
{
    /// <summary>Lista as campanhas da rede, das mais recentes para as mais antigas (US-01.9).</summary>
    [HttpGet]
    [ValidateTenantScope]
    [ProducesResponseType<IReadOnlyList<CampaignResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CampaignResponse>>> GetAll(
        [FromQuery, Required(ErrorMessage = CampaignContract.NetworkRequired)] Guid? networkId,
        [FromQuery] string? status,
        CancellationToken cancellationToken) =>
        Ok(await campaignService.GetAllAsync(networkId, status, cancellationToken));

    /// <summary>Detalhe da campanha (US-01.9); campanha de outra rede responde 403.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CampaignResponse>> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await campaignService.GetByIdAsync(id, cancellationToken));

    /// <summary>Cadastra a campanha em rascunho (US-01.1). Datas na hora local da rede, sem fuso.</summary>
    [HttpPost]
    [ValidateTenantScope]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CampaignResponse>> Create(CreateCampaignRequest request, CancellationToken cancellationToken)
    {
        var campaign = await campaignService.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/campaigns/{campaign.Id}", campaign);
    }
}
