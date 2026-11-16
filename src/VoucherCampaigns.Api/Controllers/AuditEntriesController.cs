using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Api.Filters;
using VoucherCampaigns.Application.Audit;

namespace VoucherCampaigns.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.ManageCampaigns)]
[Route("api/v1/audit-entries")]
public class AuditEntriesController(IAuditQueryService audit) : ControllerBase
{
    [HttpGet]
    [ValidateTenantScope]
    [ProducesResponseType<IReadOnlyList<AuditEntryListItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AuditEntryListItemResponse>>> GetAll(
        [FromQuery, Required(ErrorMessage = AuditQueryContract.NetworkRequired)] Guid? networkId,
        [FromQuery] string? eventType,
        [FromQuery] Guid? campaignId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken) =>
        Ok(await audit.ListAsync(networkId, eventType, campaignId, from, to, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AuditEntryDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AuditEntryDetailResponse>> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await audit.GetByIdAsync(id, cancellationToken));
}
