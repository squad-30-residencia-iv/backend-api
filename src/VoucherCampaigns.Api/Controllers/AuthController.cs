using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Errors;
using VoucherCampaigns.Application.Authentication;

namespace VoucherCampaigns.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    /// <summary>Login provisório: valida contra Auth:MockUsers até a origem das contas ser definida.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public ActionResult<TokenResponse> Login(LoginRequest request)
    {
        var response = authService.Authenticate(request);

        if (response is null)
        {
            return ApiProblems.Result(
                HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.InvalidCredentials, ApiProblems.InvalidCredentialsDetail);
        }

        return Ok(response);
    }
}
