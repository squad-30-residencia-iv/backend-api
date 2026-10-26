namespace VoucherCampaigns.Domain.Organization;

public static class OrganizationErrors
{
    public const string NetworkNameTakenCode = "NETWORK_NAME_TAKEN";
    public const string NetworkNameTakenMessage = "Já existe uma rede cadastrada com este nome.";

    public const string RegionNetworkNotFoundCode = "REGION_NETWORK_NOT_FOUND";
    public const string RegionNetworkNotFoundMessage = "Rede informada não encontrada";

    public const string RegionNetworkInactiveCode = "REGION_NETWORK_INACTIVE";
    public const string RegionNetworkInactiveMessage = "Rede informada está inativa";

    public const string RegionNameTakenCode = "REGION_NAME_TAKEN";
    public const string RegionNameTakenMessage = "Já existe uma região com este nome nesta rede.";

    /// <summary>Mensagem exata da US-02.1, cenário 2.</summary>
    public const string EstablishmentRegionNotFoundCode = "ESTABLISHMENT_REGION_NOT_FOUND";
    public const string EstablishmentRegionNotFoundMessage = "Região informada não encontrada";

    public const string EstablishmentRegionInactiveCode = "ESTABLISHMENT_REGION_INACTIVE";
    public const string EstablishmentRegionInactiveMessage = "Região informada está inativa";

    public const string EstablishmentCodeTakenCode = "ESTABLISHMENT_CODE_TAKEN";
    public const string EstablishmentCodeTakenMessage = "Já existe um estabelecimento com este código nesta região.";
}
