namespace VoucherCampaigns.Domain.Catalog;

public static class CatalogErrors
{
    public const string CategoryNameTakenCode = "CATEGORY_NAME_TAKEN";
    public const string CategoryNameTakenMessage = "Já existe uma categoria cadastrada com este nome.";

    /// <summary>Mensagem exata da US-02.4, cenário 2.</summary>
    public const string ProductCategoryNotFoundCode = "PRODUCT_CATEGORY_NOT_FOUND";
    public const string ProductCategoryNotFoundMessage = "Categoria informada não encontrada";

    public const string ProductCategoryInactiveCode = "PRODUCT_CATEGORY_INACTIVE";
    public const string ProductCategoryInactiveMessage = "Categoria informada está inativa";

    /// <summary>Mensagem exata da US-02.4, cenário 3.</summary>
    public const string ProductCodeTakenCode = "PRODUCT_CODE_TAKEN";
    public const string ProductCodeTakenMessage = "Já existe produto com este identificador";
}
