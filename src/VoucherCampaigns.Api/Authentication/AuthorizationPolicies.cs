namespace VoucherCampaigns.Api.Authentication;

public static class AuthorizationPolicies
{
    /// <summary>Criar, editar e submeter campanhas: analista e gestor.</summary>
    public const string ManageCampaigns = "ManageCampaigns";

    /// <summary>Aprovar ou reprovar campanhas: somente gestor (PR-05).</summary>
    public const string ApproveCampaigns = "ApproveCampaigns";

    /// <summary>Consultar a hierarquia: administrador, analista e gestor (estes só na própria rede).</summary>
    public const string ReadHierarchy = "ReadHierarchy";

    /// <summary>Cadastrar, alterar e inativar a hierarquia: somente administrador da plataforma (PR-15).</summary>
    public const string ManageHierarchy = "ManageHierarchy";
}
