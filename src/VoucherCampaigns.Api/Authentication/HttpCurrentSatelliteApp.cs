using System.Security.Claims;
using VoucherCampaigns.Application.Authentication;

namespace VoucherCampaigns.Api.Authentication;

/// <summary>Lê a aplicação satélite das claims do token validado na requisição atual.</summary>
public class HttpCurrentSatelliteApp(IHttpContextAccessor httpContextAccessor) : ICurrentSatelliteApp
{
    public Guid ApplicationId => Guid.Parse(Claim(AuthClaimTypes.UserId));

    public string ClientId => Claim(AuthClaimTypes.ClientId);

    public Guid NetworkId => Guid.Parse(Claim(AuthClaimTypes.NetworkId));

    public IReadOnlyList<string> Scopes => Principal().FindAll(AuthClaimTypes.Scope).Select(claim => claim.Value).ToList();

    private ClaimsPrincipal Principal()
    {
        var user = httpContextAccessor.HttpContext?.User;

        if (user is null || !SatelliteAuthentication.IsSatellite(user))
        {
            throw new InvalidOperationException("ICurrentSatelliteApp usado fora de uma requisição autenticada por aplicação satélite.");
        }

        return user;
    }

    private string Claim(string type) =>
        Principal().FindFirst(type)?.Value
        ?? throw new InvalidOperationException($"O token da aplicação não contém a claim '{type}'.");
}
