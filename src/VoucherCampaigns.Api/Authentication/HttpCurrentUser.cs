using VoucherCampaigns.Application.Authentication;

namespace VoucherCampaigns.Api.Authentication;

/// <summary>Lê o usuário das claims do token validado na requisição atual.</summary>
public class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid UserId => Guid.Parse(Claim(AuthClaimTypes.UserId));

    public string Role => Claim(AuthClaimTypes.Role);

    public Guid NetworkId => Guid.Parse(Claim(AuthClaimTypes.NetworkId));

    private string Claim(string type)
    {
        var user = httpContextAccessor.HttpContext?.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            throw new InvalidOperationException("ICurrentUser usado fora de uma requisição autenticada.");
        }

        return user.FindFirst(type)?.Value
            ?? throw new InvalidOperationException($"O token autenticado não contém a claim '{type}'.");
    }
}
