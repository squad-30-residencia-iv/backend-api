using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Api.Errors;
using VoucherCampaigns.Application.Authentication;

namespace VoucherCampaigns.Api.Controllers;

/// <summary>Acesso das aplicações satélites (US-07.5). As rotas do motor (issue #27) entram neste prefixo.</summary>
[ApiController]
[Route("api/v1/satellite")]
public class SatelliteController(ISatelliteTokenService tokenService) : ControllerBase
{
    /// <summary>Troca o clientId e o clientSecret da aplicação por um token de acesso (client credentials).</summary>
    [AllowAnonymous]
    [HttpPost("token")]
    [ProducesResponseType<SatelliteTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SatelliteTokenResponse>> Token(SatelliteTokenRequest request, CancellationToken cancellationToken)
    {
        var response = await tokenService.IssueAsync(request, cancellationToken);

        if (response is null)
        {
            return ApiProblems.Result(
                HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.InvalidClient, ApiProblems.InvalidClientDetail);
        }

        return Ok(response);
    }

    /// <summary>Aplicação do token, para o integrador conferir a credencial.</summary>
    [Authorize(Policy = AuthorizationPolicies.SatelliteApp)]
    [HttpGet("me")]
    [ProducesResponseType<SatelliteAppResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public ActionResult<SatelliteAppResponse> Me([FromServices] ICurrentSatelliteApp app) =>
        Ok(new SatelliteAppResponse(app.ApplicationId, app.ClientId, app.NetworkId, app.Scopes));
}
