using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.DTOs.Catalog;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Domain.Common;

namespace VoucherCampaigns.Application.Catalog;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryResponse>> GetAllAsync(bool? activeOnly, CancellationToken cancellationToken = default);

    Task<CategoryResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default);

    Task<CategoryResponse> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken = default);

    Task InactivateAsync(Guid id, CancellationToken cancellationToken = default);
}

public class CategoryService(ICatalogRepository repository, TimeProvider timeProvider) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryResponse>> GetAllAsync(bool? activeOnly, CancellationToken cancellationToken = default)
    {
        var items = await repository.GetAllCategoriesAsync(activeOnly, cancellationToken);
        return items.Select(Map).ToList();
    }

    public async Task<CategoryResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await repository.GetCategoryByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(CatalogContract.CategoryNotFound);

        return Map(category);
    }

    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var name = Name(request.Name);

        if (await repository.ExistsCategoryByNameAsync(name, null, cancellationToken))
        {
            throw new BusinessRuleException(CatalogErrors.CategoryNameTakenCode, CatalogErrors.CategoryNameTakenMessage);
        }

        var category = new Category
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };

        return Map(await repository.CreateCategoryAsync(category, cancellationToken));
    }

    public async Task<CategoryResponse> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetCategoryByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(CatalogContract.CategoryNotFound);

        var name = Name(request.Name);
        var isActive = InputValues.RequiredFlag(request.IsActive, nameof(request.IsActive), CatalogContract.CategoryActiveRequired);

        if (await repository.ExistsCategoryByNameAsync(name, id, cancellationToken))
        {
            throw new BusinessRuleException(CatalogErrors.CategoryNameTakenCode, CatalogErrors.CategoryNameTakenMessage);
        }

        existing.Name = name;
        existing.IsActive = isActive;

        return Map(await repository.UpdateCategoryAsync(existing, cancellationToken));
    }

    public async Task InactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetCategoryByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(CatalogContract.CategoryNotFound);

        existing.IsActive = false;
        await repository.UpdateCategoryAsync(existing, cancellationToken);
    }

    private static string Name(string value) =>
        InputValues.RequiredText(value, nameof(CreateCategoryRequest.Name), CatalogContract.CategoryNameRequired, CatalogContract.NameMaxLength, CatalogContract.NameMaxLengthMessage);

    private static CategoryResponse Map(Category category) =>
        new(category.Id, category.Name, category.IsActive, category.CreatedAt);
}
