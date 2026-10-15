using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Application.DTOs;
using VoucherCampaigns.Infrastructure.Authentication;

namespace VoucherCampaigns.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private readonly JwtAuthService _authService;

    public AuthController(JwtAuthService authService) => _authService = authService;

    [AllowAnonymous]
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var response = _authService.Authenticate(request);
        
        if (response is null)
            return Unauthorized(new { error = "Unauthorized", message = "Credenciais inválidas." });

        return Ok(response);
    }
}