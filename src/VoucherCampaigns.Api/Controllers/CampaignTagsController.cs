using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Api.Filters;
using VoucherCampaigns.Application.Campaigns;

namespace VoucherCampaigns.Api.Controllers;

/// <summary>Tags de campanha da rede (spec GH-077): o administrador mantém; analista e gestor leem.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.ReadHierarchy)]
[Route("api/v1/campaign-tags")]
public class CampaignTagsController(ICampaignTagService service) : ControllerBase
{
    [HttpGet]
    [ValidateTenantScope]
    [ProducesResponseType<IReadOnlyList<CampaignTagResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CampaignTagResponse>>> List(
        [FromQuery] Guid? networkId, [FromQuery] bool? activeOnly, CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(networkId, activeOnly, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<CampaignTagResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CampaignTagResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.ManageHierarchy)]
    [ValidateTenantScope]
    [ProducesResponseType<CampaignTagResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CampaignTagResponse>> Create([FromBody] CreateCampaignTagRequest request, CancellationToken cancellationToken)
    {
        var created = await service.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/campaign-tags/{created.Id}", created);
    }

    /// <summary>Renomeia, inativa ou reativa.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManageHierarchy)]
    [ProducesResponseType<CampaignTagResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CampaignTagResponse>> Update(Guid id, [FromBody] UpdateCampaignTagRequest request, CancellationToken cancellationToken) =>
        Ok(await service.UpdateAsync(id, request, cancellationToken));

    /// <summary>Inativa: a tag não se apaga, para as campanhas que já a usam continuarem com o nome.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManageHierarchy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await service.InactivateAsync(id, cancellationToken);
        return NoContent();
    }
}
