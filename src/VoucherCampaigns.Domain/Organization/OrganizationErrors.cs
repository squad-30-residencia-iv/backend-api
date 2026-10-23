namespace VoucherCampaigns.Domain.Organization;

public static class OrganizationErrors
{
    public const string NetworkNameTakenCode = "NETWORK_NAME_TAKEN";
    public const string NetworkNameTakenMessage = "Já existe uma rede cadastrada com este nome.";

    public const string RegionNameTakenCode = "REGION_NAME_TAKEN";
    public const string RegionNameTakenMessage = "Já existe uma região com este nome nesta rede.";

    public const string EstablishmentParentUnavailableCode = "ESTABLISHMENT_PARENT_UNAVAILABLE";
    public const string EstablishmentParentUnavailableMessage = "A região informada não existe ou está inativa.";

    public const string EstablishmentCodeTakenCode = "ESTABLISHMENT_CODE_TAKEN";
    public const string EstablishmentCodeTakenMessage = "Já existe um estabelecimento com este código nesta região.";

    public const string ChannelParentUnavailableCode = "CHANNEL_PARENT_UNAVAILABLE";
    public const string ChannelParentUnavailableMessage = "O estabelecimento informado não existe ou está inativo.";

    public const string ChannelTypeTakenCode = "CHANNEL_TYPE_TAKEN";
    public const string ChannelTypeTakenMessage = "O estabelecimento já possui um canal deste tipo.";
}
