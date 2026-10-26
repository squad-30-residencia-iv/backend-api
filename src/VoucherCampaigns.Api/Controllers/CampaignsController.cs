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

    /// <summary>Substitui os alvos, benefícios e cotas por alvo da campanha em rascunho (US-01.2, US-01.3).</summary>
    [HttpPut("{id:guid}/items")]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CampaignResponse>> ReplaceItems(
        Guid id,
        ReplaceCampaignItemsRequest request,
        CancellationToken cancellationToken) =>
        Ok(await campaignService.ReplaceItemsAsync(id, request, cancellationToken));

    /// <summary>Define a quantidade total e o limite por cliente da campanha em rascunho (US-01.3).</summary>
    [HttpPut("{id:guid}/quantities")]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CampaignResponse>> UpdateQuantities(
        Guid id,
        UpdateCampaignQuantitiesRequest request,
        CancellationToken cancellationToken) =>
        Ok(await campaignService.UpdateQuantitiesAsync(id, request, cancellationToken));

    /// <summary>Define o público elegível da campanha em rascunho (US-01.5).</summary>
    [HttpPut("{id:guid}/audience")]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CampaignResponse>> UpdateAudience(
        Guid id,
        UpdateCampaignAudienceRequest request,
        CancellationToken cancellationToken) =>
        Ok(await campaignService.UpdateAudienceAsync(id, request, cancellationToken));

    /// <summary>Edita nome, descrição e vigência da campanha em rascunho (US-01.7). Datas na hora local da rede.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CampaignResponse>> Update(
        Guid id,
        UpdateCampaignRequest request,
        CancellationToken cancellationToken) =>
        Ok(await campaignService.UpdateAsync(id, request, cancellationToken));

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
