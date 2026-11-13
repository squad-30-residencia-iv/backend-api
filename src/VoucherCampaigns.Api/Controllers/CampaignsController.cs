using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Api.Filters;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Application.Eligibility;

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

    /// <summary>Lista de clientes específicos do público restrito, paginada, em ordem de CPF (spec GH-047, D-10).</summary>
    [HttpGet("{id:guid}/audience/customers")]
    [ProducesResponseType<EligibleCustomersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EligibleCustomersResponse>> GetEligibleCustomers(
        Guid id,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        Ok(await campaignService.GetEligibleCustomersAsync(id, page, pageSize, cancellationToken));

    /// <summary>
    /// Simula o público para um cliente informado, critério a critério, sem emitir voucher e sem gravar nada
    /// (US-06.6, spec GH-047 D-17). Vale em qualquer status da campanha.
    /// </summary>
    [HttpPost("{id:guid}/eligibility/simulate")]
    [ProducesResponseType<EligibilitySimulationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EligibilitySimulationResponse>> SimulateEligibility(
        Guid id,
        EligibilitySimulationRequest request,
        CancellationToken cancellationToken) =>
        Ok(await campaignService.SimulateEligibilityAsync(id, request, cancellationToken));

    /// <summary>Define a validade do voucher (prazo relativo ou data fixa) da campanha em rascunho (US-01.12).</summary>
    [HttpPut("{id:guid}/voucher-validity")]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CampaignResponse>> UpdateVoucherValidity(
        Guid id,
        UpdateVoucherValidityRequest request,
        CancellationToken cancellationToken) =>
        Ok(await campaignService.UpdateVoucherValidityAsync(id, request, cancellationToken));

    /// <summary>Define as restrições de uso no resgate da campanha em rascunho (US-01.12).</summary>
    [HttpPut("{id:guid}/usage-restrictions")]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CampaignResponse>> UpdateUsageRestrictions(
        Guid id,
        UpdateUsageRestrictionsRequest request,
        CancellationToken cancellationToken) =>
        Ok(await campaignService.UpdateUsageRestrictionsAsync(id, request, cancellationToken));

    /// <summary>Substitui as condições de geração e o uso na mesma compra da campanha em rascunho (US-01.4).</summary>
    [HttpPut("{id:guid}/conditions")]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CampaignResponse>> ReplaceConditions(
        Guid id,
        ReplaceCampaignConditionsRequest request,
        CancellationToken cancellationToken) =>
        Ok(await campaignService.ReplaceConditionsAsync(id, request, cancellationToken));

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

    /// <summary>Submete o rascunho para aprovação (US-01.6). Somente o analista.</summary>
    [HttpPost("{id:guid}/submit")]
    [Authorize(Policy = AuthorizationPolicies.SubmitCampaigns)]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CampaignResponse>> Submit(Guid id, CancellationToken cancellationToken) =>
        Ok(await campaignService.SubmitAsync(id, cancellationToken));

    /// <summary>Aprova a campanha em análise (US-01.11). Somente o gestor.</summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = AuthorizationPolicies.ApproveCampaigns)]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CampaignResponse>> Approve(Guid id, CancellationToken cancellationToken) =>
        Ok(await campaignService.ApproveAsync(id, cancellationToken));

    /// <summary>Reprova a campanha em análise (US-01.11). Motivo opcional. Somente o gestor.</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = AuthorizationPolicies.ApproveCampaigns)]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CampaignResponse>> Reject(
        Guid id,
        RejectCampaignRequest? request,
        CancellationToken cancellationToken) =>
        Ok(await campaignService.RejectAsync(id, request, cancellationToken));

    /// <summary>Substitui onde a campanha em rascunho vale: rede, regiões ou lojas, com os canais (US-02.3).</summary>
    [HttpPut("{id:guid}/scopes")]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CampaignResponse>> ReplaceScopes(
        Guid id,
        ReplaceCampaignScopesRequest request,
        CancellationToken cancellationToken) =>
        Ok(await campaignService.ReplaceScopesAsync(id, request, cancellationToken));

    /// <summary>Cria um rascunho com a configuração da campanha, de qualquer status (US-01.10). Corpo opcional.</summary>
    [HttpPost("{id:guid}/duplicate")]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CampaignResponse>> Duplicate(
        Guid id,
        DuplicateCampaignRequest? request,
        CancellationToken cancellationToken)
    {
        var copy = await campaignService.DuplicateAsync(id, request, cancellationToken);
        return Created($"/api/v1/campaigns/{copy.Id}", copy);
    }

    /// <summary>Pausa a campanha ativa (US-01.8). Analista e gestor; não há retomada.</summary>
    [HttpPost("{id:guid}/pause")]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CampaignResponse>> Pause(Guid id, CancellationToken cancellationToken) =>
        Ok(await campaignService.PauseAsync(id, cancellationToken));

    /// <summary>Encerra a campanha ativa ou pausada (US-01.8). Analista e gestor.</summary>
    [HttpPost("{id:guid}/end")]
    [ProducesResponseType<CampaignResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CampaignResponse>> End(Guid id, CancellationToken cancellationToken) =>
        Ok(await campaignService.EndAsync(id, cancellationToken));

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
