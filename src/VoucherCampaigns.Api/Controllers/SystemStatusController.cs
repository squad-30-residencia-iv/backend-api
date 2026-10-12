using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Application.SystemStatus;

namespace VoucherCampaigns.Api.Controllers;

[ApiController]
[Route("api/v1/system")]
public class SystemStatusController(ISystemStatusService systemStatusService) : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType<SystemStatusResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemStatusResponse>> GetStatus(CancellationToken cancellationToken) =>
        Ok(await systemStatusService.GetStatusAsync(cancellationToken));
}
