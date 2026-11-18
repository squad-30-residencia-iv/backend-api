using System.ComponentModel.DataAnnotations;

namespace VoucherCampaigns.Application.DTOs.Catalog;

public static class CatalogContract
{
    public const int MaxImportLines = 1_000;
    public const string ImportProductsRequired = "Informe de 1 a 1.000 produtos.";
    public const string ImportCodeRepeated = "O código se repete na lista.";
    public const int NameMaxLength = 150;
    public const int ProductCodeMaxLength = 30;

    public const string CategoryNameRequired = "Informe o nome da categoria.";
    public const string NameMaxLengthMessage = "O nome deve ter no máximo 150 caracteres.";
    public const string CategoryActiveRequired = "Informe se a categoria está ativa.";
    public const string ProductCategoryRequired = "Informe a categoria do produto.";
    public const string ProductCodeRequired = "Informe o código do produto.";
    public const string ProductCodeMaxLengthMessage = "O código deve ter no máximo 30 caracteres.";
    public const string ProductNameRequired = "Informe o nome do produto.";
    public const string ProductActiveRequired = "Informe se o produto está ativo.";

    public const string CategoryNotFound = "Categoria não encontrada.";
    public const string ProductNotFound = "Produto não encontrado.";
}

public record CreateCategoryRequest(
    [Required(ErrorMessage = CatalogContract.CategoryNameRequired)]
    [StringLength(CatalogContract.NameMaxLength, MinimumLength = 1, ErrorMessage = CatalogContract.NameMaxLengthMessage)]
    string Name);

public record UpdateCategoryRequest(
    [Required(ErrorMessage = CatalogContract.CategoryNameRequired)]
    [StringLength(CatalogContract.NameMaxLength, MinimumLength = 1, ErrorMessage = CatalogContract.NameMaxLengthMessage)]
    string Name,
    [Required(ErrorMessage = CatalogContract.CategoryActiveRequired)]
    bool? IsActive);

public record CategoryResponse(Guid Id, string Name, bool IsActive, DateTime CreatedAt);

public record CreateProductRequest(
    [Required(ErrorMessage = CatalogContract.ProductCategoryRequired)]
    Guid? CategoryId,
    [Required(ErrorMessage = CatalogContract.ProductCodeRequired)]
    [StringLength(CatalogContract.ProductCodeMaxLength, MinimumLength = 1, ErrorMessage = CatalogContract.ProductCodeMaxLengthMessage)]
    string Code,
    [Required(ErrorMessage = CatalogContract.ProductNameRequired)]
    [StringLength(CatalogContract.NameMaxLength, MinimumLength = 1, ErrorMessage = CatalogContract.NameMaxLengthMessage)]
    string Name);

/// <summary>Produtos em lote (spec GH-066, D-3): uma categoria para todos; de 1 a 1.000 linhas.</summary>
public record ImportProductsRequest(
    [Required(ErrorMessage = CatalogContract.ProductCategoryRequired)]
    Guid? CategoryId,
    [Required(ErrorMessage = CatalogContract.ImportProductsRequired)]
    IReadOnlyList<ImportProductLine>? Products);

public record ImportProductLine(string? Code, string? Name);

public record ImportProductsResponse(int Created, IReadOnlyList<ProductResponse> Products);

public record UpdateProductRequest(
    [Required(ErrorMessage = CatalogContract.ProductCodeRequired)]
    [StringLength(CatalogContract.ProductCodeMaxLength, MinimumLength = 1, ErrorMessage = CatalogContract.ProductCodeMaxLengthMessage)]
    string Code,
    [Required(ErrorMessage = CatalogContract.ProductNameRequired)]
    [StringLength(CatalogContract.NameMaxLength, MinimumLength = 1, ErrorMessage = CatalogContract.NameMaxLengthMessage)]
    string Name,
    [Required(ErrorMessage = CatalogContract.ProductActiveRequired)]
    bool? IsActive);

public record ProductResponse(Guid Id, Guid CategoryId, string Code, string Name, bool IsActive, DateTime CreatedAt);
