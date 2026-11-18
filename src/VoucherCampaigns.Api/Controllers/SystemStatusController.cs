using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Application.SystemStatus;

namespace VoucherCampaigns.Api.Controllers;

/// <summary>Provider e migrações aplicadas: só o administrador (spec GH-066, D-8; SEC-08). O <c>/health</c> segue anônimo.</summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.ManageHierarchy)]
[Route("api/v1/system")]
public class SystemStatusController(ISystemStatusService systemStatusService) : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType<SystemStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SystemStatusResponse>> GetStatus(CancellationToken cancellationToken) =>
        Ok(await systemStatusService.GetStatusAsync(cancellationToken));
}
