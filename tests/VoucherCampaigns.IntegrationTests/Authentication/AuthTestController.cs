using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Api.Filters;
using VoucherCampaigns.Application.Authentication;

namespace VoucherCampaigns.IntegrationTests.Authentication;

public record ScopedTestRequest(Guid NetworkId) : INetworkScopedRequest;

/// <summary>Rotas protegidas para exercitar AuthGuard, policies e TenantGuard (registradas só no ApiFactory).</summary>
[ApiController]
[Route("test/auth")]
public class AuthTestController(ICurrentUser currentUser) : ControllerBase
{
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me() => Ok(new { currentUser.UserId, currentUser.Role, currentUser.NetworkId });

    [Authorize(Policy = AuthorizationPolicies.ManageCampaigns)]
    [HttpGet("manage")]
    public IActionResult Manage() => Ok();

    [Authorize(Policy = AuthorizationPolicies.ApproveCampaigns)]
    [HttpGet("approve")]
    public IActionResult Approve() => Ok();

    [Authorize(Policy = AuthorizationPolicies.ManageCampaigns)]
    [ValidateTenantScope]
    [HttpGet("networks/{networkId:guid}")]
    public IActionResult ByRoute(Guid networkId) => Ok(networkId);

    [Authorize(Policy = AuthorizationPolicies.ManageCampaigns)]
    [ValidateTenantScope]
    [HttpPost("scoped")]
    public IActionResult ByBody(ScopedTestRequest request) => Ok(request.NetworkId);

    [Authorize]
    [ValidateTenantScope]
    [HttpGet("without-network")]
    public IActionResult WithoutNetwork() => Ok();
}
