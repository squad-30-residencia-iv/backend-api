using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Application.Eligibility;

namespace VoucherCampaigns.Api.Controllers;

/// <summary>Catálogo do público restrito para o portal (spec GH-075, D-2). Igual para todas as redes.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.ManageCampaigns)]
[Route("api/v1/eligibility")]
public class EligibilityController : ControllerBase
{
    private static readonly EligibilityCatalogResponse Catalog = EligibilityCatalog.Build();

    /// <summary>Tipos de critério, campos, limites e os atributos que o satélite precisa mandar para cada um.</summary>
    [HttpGet("criteria")]
    [ProducesResponseType<EligibilityCatalogResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public ActionResult<EligibilityCatalogResponse> Criteria() => Ok(Catalog);
}
