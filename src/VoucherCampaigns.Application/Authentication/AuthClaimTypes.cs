namespace VoucherCampaigns.Application.Authentication;

/// <summary>Nomes das claims do token, compartilhados entre emissão e leitura.</summary>
public static class AuthClaimTypes
{
    public const string UserId = "sub";
    public const string Role = "role";
    public const string NetworkId = "escopoId";

    /// <summary>Identificador público da aplicação satélite (US-07.5); só no token de aplicação.</summary>
    public const string ClientId = "client_id";

    /// <summary>Escopos da aplicação satélite; uma claim por escopo.</summary>
    public const string Scope = "scope";
}
