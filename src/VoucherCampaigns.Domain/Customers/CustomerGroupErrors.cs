namespace VoucherCampaigns.Domain.Customers;

public static class CustomerGroupErrors
{
    public const string NameTakenCode = "CUSTOMER_GROUP_NAME_TAKEN";
    public const string NameTakenMessage = "Já existe um grupo com este nome nesta rede.";

    public const string NotFoundCode = "CUSTOMER_GROUP_NOT_FOUND";
    public const string NotFoundMessage = "Grupo informado não encontrado.";

    public const string InactiveCode = "CUSTOMER_GROUP_INACTIVE";
    public const string InactiveMessage = "Grupo informado está inativo.";

    public const string NetworkInactiveCode = "CUSTOMER_GROUP_NETWORK_INACTIVE";
    public const string NetworkInactiveMessage = "Rede informada está inativa.";
}
