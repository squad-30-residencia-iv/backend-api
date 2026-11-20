using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    [EnableRateLimiting(SatelliteTokenRateLimitOptions.PolicyName)]
    [ProducesResponseType<SatelliteTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<SatelliteTokenResponse>> Token(
        SatelliteTokenRequest request,
        [FromServices] ISatelliteTokenRateLimiter rateLimiter,
        CancellationToken cancellationToken)
    {
        var clientId = request.ClientId?.Trim().ToLowerInvariant() ?? string.Empty;

        // O bloqueio é por clientId + IP (spec GH-071, D-2): errar o segredo de outra origem não trava o PDV legítimo.
        var origin = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (rateLimiter.IsLockedOut(clientId, origin))
        {
            return ApiProblems.Result(
                HttpContext,
                StatusCodes.Status429TooManyRequests,
                "SATELLITE_TOKEN_RATE_LIMIT_EXCEEDED",
                "Limite de tentativas excedido para este clientId. Aguarde alguns instantes antes de tentar novamente.");
        }

        var response = await tokenService.IssueAsync(request, cancellationToken);

        if (response is null)
        {
            rateLimiter.RecordFailure(clientId, origin);
            return ApiProblems.Result(
                HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.InvalidClient, ApiProblems.InvalidClientDetail);
        }

        rateLimiter.Reset(clientId, origin);
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
