namespace VoucherCampaigns.Api.Authentication;

public static class AuthorizationPolicies
{
    /// <summary>Criar, editar e submeter campanhas: analista e gestor.</summary>
    public const string ManageCampaigns = "ManageCampaigns";

    /// <summary>Aprovar ou reprovar campanhas: somente gestor (PR-05).</summary>
    public const string ApproveCampaigns = "ApproveCampaigns";
}
