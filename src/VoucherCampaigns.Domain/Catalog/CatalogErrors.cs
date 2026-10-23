namespace VoucherCampaigns.Domain.Catalog;

public static class CatalogErrors
{
    public const string CategoryNameTakenCode = "CATEGORY_NAME_TAKEN";
    public const string CategoryNameTakenMessage = "Já existe uma categoria cadastrada com este nome.";

    public const string ProductParentUnavailableCode = "PRODUCT_PARENT_UNAVAILABLE";
    public const string ProductParentUnavailableMessage = "A categoria informada não existe ou está inativa.";

    public const string ProductCodeTakenCode = "PRODUCT_CODE_TAKEN";
    public const string ProductCodeTakenMessage = "Já existe um produto cadastrado com este código.";
}
