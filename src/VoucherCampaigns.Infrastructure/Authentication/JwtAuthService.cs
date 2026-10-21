using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using VoucherCampaigns.Application.Authentication;

namespace VoucherCampaigns.Infrastructure.Authentication;

public class JwtAuthService(AuthOptions options, IUserCredentialStore credentialStore, TimeProvider timeProvider) : IAuthService
{
    public const string SuccessMessage = "Autenticação realizada com sucesso.";

    public TokenResponse? Authenticate(LoginRequest request)
    {
        var account = credentialStore.FindByCredentials(request.Email, request.Password);

        if (account is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var lifetime = TimeSpan.FromMinutes(options.ExpirationMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(lifetime),
            Claims = new Dictionary<string, object>
            {
                [AuthClaimTypes.UserId] = account.UserId.ToString(),
                [AuthClaimTypes.Role] = account.Role,
                [AuthClaimTypes.NetworkId] = account.NetworkId.ToString()
            },
            SigningCredentials = new SigningCredentials(CreateSigningKey(options), SecurityAlgorithms.HmacSha256)
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);

        return new TokenResponse(token, (int)lifetime.TotalSeconds, SuccessMessage);
    }

    public static SymmetricSecurityKey CreateSigningKey(AuthOptions options) =>
        new(Encoding.UTF8.GetBytes(options.SigningKey));
}
