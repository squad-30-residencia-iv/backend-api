using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Api.Filters;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Application.Dashboard;

namespace VoucherCampaigns.Api.Controllers;

/// <summary>Acompanhamento das campanhas da rede (EP-03, US-03.2, US-03.3, US-03.5): analista e gestor da própria rede.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.ManageCampaigns)]
[Route("api/v1/dashboard")]
public class DashboardController(IDashboardService dashboardService) : ControllerBase
{
    /// <summary>Todas as campanhas da rede, em qualquer status, com indicadores resumidos e totais, no recorte dos filtros.</summary>
    [HttpGet]
    [ValidateTenantScope]
    [ProducesResponseType<NetworkDashboardResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NetworkDashboardResponse>> Get(
        [FromQuery, Required(ErrorMessage = CampaignContract.NetworkRequired)] Guid? networkId,
        [FromQuery] string? status,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? establishmentId,
        [FromQuery] Guid? regionId,
        [FromQuery] Guid? categoryId,
        [FromQuery] string? audience,
        CancellationToken cancellationToken) =>
        Ok(await dashboardService.GetNetworkAsync(
            networkId, status, new DashboardFilters(from, to, establishmentId, regionId, categoryId, audience), cancellationToken));

    /// <summary>A mesma visão em CSV (D8); sem campanha no recorte responde 422 EXPORT_EMPTY.</summary>
    [HttpGet("export")]
    [ValidateTenantScope]
    [Produces(CsvFile.ContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Export(
        [FromQuery, Required(ErrorMessage = CampaignContract.NetworkRequired)] Guid? networkId,
        [FromQuery] string? status,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? establishmentId,
        [FromQuery] Guid? regionId,
        [FromQuery] Guid? categoryId,
        [FromQuery] string? audience,
        CancellationToken cancellationToken)
    {
        var file = await dashboardService.ExportNetworkAsync(
            networkId, status, new DashboardFilters(from, to, establishmentId, regionId, categoryId, audience), cancellationToken);

        return File(file.Content, CsvFile.ContentType, file.FileName);
    }
}

/// <summary>Dashboard e trilha de auditoria de uma campanha (US-03.1, US-03.4, US-03.5); campanha de outra rede responde 403.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.ManageCampaigns)]
[Route("api/v1/campaigns/{id:guid}")]
public class CampaignDashboardController(IDashboardService dashboardService) : ControllerBase
{
    /// <summary>Indicadores da campanha no recorte dos filtros; zerados quando não há movimentação.</summary>
    [HttpGet("dashboard")]
    [ProducesResponseType<CampaignDashboardResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CampaignDashboardResponse>> Dashboard(
        Guid id,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? establishmentId,
        [FromQuery] Guid? regionId,
        [FromQuery] Guid? categoryId,
        CancellationToken cancellationToken) =>
        Ok(await dashboardService.GetCampaignAsync(id, new DashboardFilters(from, to, establishmentId, regionId, categoryId, null), cancellationToken));

    /// <summary>Operações de configuração e ciclo de vida da campanha, em ordem cronológica, com o responsável.</summary>
    [HttpGet("audit")]
    [ProducesResponseType<CampaignAuditResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CampaignAuditResponse>> Audit(Guid id, CancellationToken cancellationToken) =>
        Ok(await dashboardService.GetAuditAsync(id, cancellationToken));

    /// <summary>A trilha em CSV (D8).</summary>
    [HttpGet("audit/export")]
    [Produces(CsvFile.ContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportAudit(Guid id, CancellationToken cancellationToken)
    {
        var file = await dashboardService.ExportAuditAsync(id, cancellationToken);

        return File(file.Content, CsvFile.ContentType, file.FileName);
    }
}
