using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Errors;
using VoucherCampaigns.Application.Authentication;

namespace VoucherCampaigns.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    /// <summary>Valida e-mail e senha contra a tabela users.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var response = await authService.AuthenticateAsync(request, cancellationToken);

        if (response is null)
        {
            return ApiProblems.Result(
                HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.InvalidCredentials, ApiProblems.InvalidCredentialsDetail);
        }

        return Ok(response);
    }
}
