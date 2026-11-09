namespace VoucherCampaigns.Api.Authentication;

public static class AuthorizationPolicies
{
    /// <summary>Criar e editar campanhas: analista e gestor. Submeter é a policy <see cref="SubmitCampaigns"/>.</summary>
    public const string ManageCampaigns = "ManageCampaigns";

    /// <summary>Submeter o rascunho para aprovação: somente analista (US-01.6).</summary>
    public const string SubmitCampaigns = "SubmitCampaigns";

    /// <summary>Aprovar ou reprovar campanhas: somente gestor (PR-05).</summary>
    public const string ApproveCampaigns = "ApproveCampaigns";

    /// <summary>Consultar a hierarquia: administrador, analista e gestor (estes só na própria rede).</summary>
    public const string ReadHierarchy = "ReadHierarchy";

    /// <summary>Cadastrar, alterar e inativar a hierarquia: somente administrador da plataforma (PR-15).</summary>
    public const string ManageHierarchy = "ManageHierarchy";

    /// <summary>Qualquer aplicação satélite autenticada (US-07.5); token de usuário não passa.</summary>
    public const string SatelliteApp = "SatelliteApp";

    /// <summary>Aplicação satélite com o escopo <c>campaigns:read</c> (US-07.1).</summary>
    public const string SatelliteReadCampaigns = "SatelliteReadCampaigns";

    /// <summary>Aplicação satélite com o escopo <c>vouchers:issue</c> (US-07.2, US-07.4).</summary>
    public const string SatelliteIssueVouchers = "SatelliteIssueVouchers";

    /// <summary>Aplicação satélite com o escopo <c>vouchers:redeem</c> (US-07.3).</summary>
    public const string SatelliteRedeemVouchers = "SatelliteRedeemVouchers";
}
