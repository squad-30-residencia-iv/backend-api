using System.Security.Claims;

namespace VoucherCampaigns.Api.Authentication;

/// <summary>Esquema de autenticação das aplicações satélites (US-07.5), separado do JWT de usuário.</summary>
public static class SatelliteAuthentication
{
    public const string Scheme = "Satellite";

    /// <summary>O token validado é de aplicação satélite (o esquema grava o próprio nome na identidade).</summary>
    public static bool IsSatellite(ClaimsPrincipal principal) =>
        principal.Identities.Any(identity => identity.IsAuthenticated && identity.AuthenticationType == Scheme);
}
