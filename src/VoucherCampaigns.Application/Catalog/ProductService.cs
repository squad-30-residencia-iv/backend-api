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

    Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken = default);

    Task InactivateAsync(Guid id, CancellationToken cancellationToken = default);
}

public class ProductService(ICatalogRepository repository, TimeProvider timeProvider) : IProductService
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
