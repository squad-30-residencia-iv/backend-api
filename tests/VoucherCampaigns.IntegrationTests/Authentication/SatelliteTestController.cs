using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Api.Filters;
using VoucherCampaigns.Application.Authentication;

namespace VoucherCampaigns.IntegrationTests.Authentication;

/// <summary>Rotas de satélite para exercitar escopos e TenantGuard (registradas só no ApiFactory).</summary>
[ApiController]
[Route("test/satellite")]
public class SatelliteTestController(ICurrentSatelliteApp app) : ControllerBase
{
    [Authorize(Policy = AuthorizationPolicies.SatelliteIssueVouchers)]
    [HttpPost("issue")]
    public IActionResult Issue() => Ok(app.ClientId);

    [Authorize(Policy = AuthorizationPolicies.SatelliteReadCampaigns)]
    [ValidateTenantScope]
    [HttpGet("networks/{networkId:guid}")]
    public IActionResult ByNetwork(Guid networkId) => Ok(networkId);
}
