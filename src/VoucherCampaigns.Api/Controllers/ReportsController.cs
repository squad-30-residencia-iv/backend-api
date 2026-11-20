using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Api.Filters;
using VoucherCampaigns.Application.Reports;

namespace VoucherCampaigns.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.ManageCampaigns)]
[Route("api/v1/reports")]
public class ReportsController(IReportService reports) : ControllerBase
{
    [HttpGet("overview")]
    [ValidateTenantScope]
    [ProducesResponseType<ReportOverviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReportOverviewResponse>> Overview(
        [FromQuery, Required(ErrorMessage = ReportContract.NetworkRequired)] Guid? networkId,
        [FromQuery] string? period,
        [FromQuery] Guid? campaignId,
        [FromQuery] Guid? regionId,
        [FromQuery] Guid? establishmentId,
        CancellationToken cancellationToken) =>
        Ok(await reports.GetOverviewAsync(networkId, period, campaignId, regionId, establishmentId, cancellationToken));

    [HttpGet("catalog")]
    [ValidateTenantScope]
    [ProducesResponseType<ReportCatalogResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReportCatalogResponse>> Catalog(
        [FromQuery, Required(ErrorMessage = ReportContract.NetworkRequired)] Guid? networkId,
        CancellationToken cancellationToken) =>
        Ok(await reports.GetCatalogAsync(networkId, cancellationToken));
}
