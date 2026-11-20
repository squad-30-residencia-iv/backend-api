using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.DTOs.Catalog;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Domain.Common;

namespace VoucherCampaigns.Application.Catalog;

public interface IProductService
{
    Task<IReadOnlyList<ProductResponse>> GetAllAsync(Guid? categoryId, bool? activeOnly, CancellationToken cancellationToken = default);

    Task<ProductResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default);

    Task<ImportProductsResponse> ImportAsync(ImportProductsRequest request, CancellationToken cancellationToken = default);

    Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken = default);

    Task InactivateAsync(Guid id, CancellationToken cancellationToken = default);
}

public class ProductService(ICatalogRepository repository, IProductImportStore importStore, TimeProvider timeProvider) : IProductService
{
    public async Task<IReadOnlyList<ProductResponse>> GetAllAsync(Guid? categoryId, bool? activeOnly, CancellationToken cancellationToken = default)
    {
        if (categoryId == Guid.Empty)
        {
            throw new RequestValidationException(nameof(CreateProductRequest.CategoryId), CatalogContract.ProductCategoryRequired);
        }

        var items = await repository.GetProductsAsync(categoryId, activeOnly, cancellationToken);
        return items.Select(Map).ToList();
    }

    public async Task<ProductResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await repository.GetProductByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(CatalogContract.ProductNotFound);

        return Map(product);
    }

    public async Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        var categoryId = InputValues.RequiredId(request.CategoryId, nameof(request.CategoryId), CatalogContract.ProductCategoryRequired);
        var code = Code(request.Code);
        var name = Name(request.Name);

        await RequireActiveCategoryAsync(categoryId, cancellationToken);

        if (await repository.ExistsProductByCodeAsync(code, null, cancellationToken))
        {
            throw new BusinessRuleException(CatalogErrors.ProductCodeTakenCode, CatalogErrors.ProductCodeTakenMessage);
        }

        var product = new Product
        {
            Id = Guid.CreateVersion7(),
            CategoryId = categoryId,
            Code = code,
            Name = name,
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };

        return Map(await repository.CreateProductAsync(product, cancellationToken));
    }

    /// <summary>
    /// Produtos em lote (spec GH-066, D-3; ponto 3 do cliente): as mesmas regras do cadastro um a um, linha a linha, e tudo
    /// ou nada. O erro aponta a linha (<c>Products[i].Code</c>); código já existente no catálogo recusa o lote com 422.
    /// </summary>
    public async Task<ImportProductsResponse> ImportAsync(ImportProductsRequest request, CancellationToken cancellationToken = default)
    {
        var categoryId = InputValues.RequiredId(request.CategoryId, nameof(request.CategoryId), CatalogContract.ProductCategoryRequired);
        var lines = request.Products;

        if (lines is null || lines.Count is 0 or > CatalogContract.MaxImportLines)
        {
            throw new RequestValidationException(nameof(request.Products), CatalogContract.ImportProductsRequired);
        }

        var parsed = new List<(string Code, string Name)>(lines.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < lines.Count; index++)
        {
            var field = $"{nameof(request.Products)}[{index}]";
            var code = InputValues.RequiredText(
                lines[index]?.Code ?? string.Empty, $"{field}.Code", CatalogContract.ProductCodeRequired,
                CatalogContract.ProductCodeMaxLength, CatalogContract.ProductCodeMaxLengthMessage);
            var name = InputValues.RequiredText(
                lines[index]?.Name ?? string.Empty, $"{field}.Name", CatalogContract.ProductNameRequired,
                CatalogContract.NameMaxLength, CatalogContract.NameMaxLengthMessage);

            if (!seen.Add(code))
            {
                throw new RequestValidationException($"{field}.Code", CatalogContract.ImportCodeRepeated);
            }

            parsed.Add((code, name));
        }

        await RequireActiveCategoryAsync(categoryId, cancellationToken);

        var taken = await importStore.GetExistingCodesAsync(seen, cancellationToken);

        if (taken.Count > 0)
        {
            throw new BusinessRuleException(
                CatalogErrors.ProductCodeTakenCode,
                CatalogErrors.ProductCodeTakenMessage,
                new Dictionary<string, object?> { ["codes"] = taken.Order(StringComparer.Ordinal).ToList() });
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var products = parsed.Select(line => new Product
        {
            Id = Guid.CreateVersion7(),
            CategoryId = categoryId,
            Code = line.Code,
            Name = line.Name,
            IsActive = true,
            CreatedAt = now
        }).ToList();

        await importStore.AddAllAsync(products, cancellationToken);

        return new ImportProductsResponse(products.Count, products.Select(Map).ToList());
    }

    public async Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetProductByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(CatalogContract.ProductNotFound);

        var code = Code(request.Code);
        var name = Name(request.Name);
        var isActive = InputValues.RequiredFlag(request.IsActive, nameof(request.IsActive), CatalogContract.ProductActiveRequired);

        if (isActive)
        {
            await RequireActiveCategoryAsync(existing.CategoryId, cancellationToken);
        }

        if (await repository.ExistsProductByCodeAsync(code, id, cancellationToken))
        {
            throw new BusinessRuleException(CatalogErrors.ProductCodeTakenCode, CatalogErrors.ProductCodeTakenMessage);
        }

        existing.Code = code;
        existing.Name = name;
        existing.IsActive = isActive;

        return Map(await repository.UpdateProductAsync(existing, cancellationToken));
    }

    public async Task InactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetProductByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(CatalogContract.ProductNotFound);

        existing.IsActive = false;
        await repository.UpdateProductAsync(existing, cancellationToken);
    }

    private async Task RequireActiveCategoryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        var category = await repository.GetCategoryByIdAsync(categoryId, cancellationToken)
            ?? throw new BusinessRuleException(CatalogErrors.ProductCategoryNotFoundCode, CatalogErrors.ProductCategoryNotFoundMessage);

        if (!category.IsActive)
        {
            throw new BusinessRuleException(CatalogErrors.ProductCategoryInactiveCode, CatalogErrors.ProductCategoryInactiveMessage);
        }
    }

    private static string Code(string value) =>
        InputValues.RequiredText(value, nameof(CreateProductRequest.Code), CatalogContract.ProductCodeRequired, CatalogContract.ProductCodeMaxLength, CatalogContract.ProductCodeMaxLengthMessage);

    private static string Name(string value) =>
        InputValues.RequiredText(value, nameof(CreateProductRequest.Name), CatalogContract.ProductNameRequired, CatalogContract.NameMaxLength, CatalogContract.NameMaxLengthMessage);

    private static ProductResponse Map(Product product) =>
        new(product.Id, product.CategoryId, product.Code, product.Name, product.IsActive, product.CreatedAt);
}
