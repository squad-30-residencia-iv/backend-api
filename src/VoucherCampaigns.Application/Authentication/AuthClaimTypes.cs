namespace VoucherCampaigns.Application.Authentication;

/// <summary>Nomes das claims do token, compartilhados entre emissão e leitura.</summary>
public static class AuthClaimTypes
{
    public const string UserId = "sub";
    public const string Role = "role";
    public const string NetworkId = "escopoId";
}
