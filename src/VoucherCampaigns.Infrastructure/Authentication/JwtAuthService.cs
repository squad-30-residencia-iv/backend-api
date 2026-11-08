using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using VoucherCampaigns.Application.Authentication;

namespace VoucherCampaigns.Infrastructure.Authentication;

public class JwtAuthService(AuthOptions options, IUserCredentialStore credentialStore, TimeProvider timeProvider) : IAuthService
{
    public const string SuccessMessage = "Autenticação realizada com sucesso.";

    public async Task<TokenResponse?> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var account = await credentialStore.FindByCredentialsAsync(request.Email, request.Password, cancellationToken);

        if (account is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var lifetime = TimeSpan.FromMinutes(options.ExpirationMinutes);

        var claims = new Dictionary<string, object>
        {
            [AuthClaimTypes.UserId] = account.UserId.ToString(),
            [AuthClaimTypes.Role] = account.Role
        };

        // O administrador da plataforma não tem rede: o token sai sem escopoId.
        if (account.NetworkId is { } networkId)
        {
            claims[AuthClaimTypes.NetworkId] = networkId.ToString();
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(lifetime),
            Claims = claims,
            SigningCredentials = new SigningCredentials(CreateSigningKey(options), SecurityAlgorithms.HmacSha256)
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        var profile = new UserProfileResponse(account.UserId, account.Email, account.Role, account.NetworkId);

        return new TokenResponse(token, (int)lifetime.TotalSeconds, SuccessMessage, profile);
    }

    public static SymmetricSecurityKey CreateSigningKey(AuthOptions options) =>
        new(Encoding.UTF8.GetBytes(options.SigningKey));
}
