using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using VoucherCampaigns.Application.Authentication;

namespace VoucherCampaigns.Infrastructure.Authentication;

/// <summary>
/// Token da aplicação satélite (US-07.5): mesmo emissor e chave do token de usuário, audiência própria,
/// sem papel. Claims: <c>sub</c> (aplicação), <c>client_id</c>, <c>escopoId</c> (rede) e <c>scope</c> (lista).
/// </summary>
public class SatelliteTokenService(
    AuthOptions authOptions,
    SatelliteAuthOptions satelliteOptions,
    ISatelliteCredentialStore credentialStore,
    TimeProvider timeProvider) : ISatelliteTokenService
{
    public const string TokenType = "Bearer";

    public async Task<SatelliteTokenResponse?> IssueAsync(SatelliteTokenRequest request, CancellationToken cancellationToken)
    {
        var app = await credentialStore.FindByCredentialsAsync(request.ClientId, request.ClientSecret, cancellationToken);

        if (app is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var lifetime = TimeSpan.FromMinutes(satelliteOptions.ExpirationMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = authOptions.Issuer,
            Audience = satelliteOptions.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(lifetime),
            Claims = new Dictionary<string, object>
            {
                [AuthClaimTypes.UserId] = app.ApplicationId.ToString(),
                [AuthClaimTypes.ClientId] = app.ClientId,
                [AuthClaimTypes.NetworkId] = app.NetworkId.ToString(),
                [AuthClaimTypes.Scope] = app.Scopes.ToArray()
            },
            SigningCredentials = new SigningCredentials(JwtAuthService.CreateSigningKey(authOptions), SecurityAlgorithms.HmacSha256)
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);

        return new SatelliteTokenResponse(token, TokenType, (int)lifetime.TotalSeconds, app.Scopes);
    }
}
