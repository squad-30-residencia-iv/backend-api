using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using VoucherCampaigns.Application.DTOs;

namespace VoucherCampaigns.Infrastructure.Authentication;

public class JwtAuthService
{
    private const string SecretKey = "chave-super-secreta-para-assinatura-jwt-2026-vouchers"; 

    public TokenResponse? Authenticate(LoginRequest request)
    {
        // Mock provisório para aprovação da US. (No futuro, injetar IUserRepository)
        if (request.Email != "gestor@empresa.com" || request.Password != "Senha123") return null;

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(SecretKey);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "usr_12345"),
                new Claim(ClaimTypes.Role, "gestor"),
                new Claim("escopoId", "tenant_abc") // Isolamento de dados
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);

        return new TokenResponse(tokenHandler.WriteToken(token), 3600, "Autenticação realizada com sucesso.");
    }
}