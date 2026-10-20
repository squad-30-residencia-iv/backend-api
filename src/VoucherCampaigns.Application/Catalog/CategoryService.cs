using VoucherCampaigns.Application.DTOs.Catalog;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Catalog;

namespace VoucherCampaigns.Application.Catalog;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryResponse>> GetAllAsync(bool? activeOnly, CancellationToken ct = default);
    Task<CategoryResponse?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken ct = default);
    Task<CategoryResponse?> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct = default);
    Task<bool> InactivateAsync(Guid id, CancellationToken ct = default);
}

public class CategoryService(ICatalogRepository repository) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryResponse>> GetAllAsync(bool? activeOnly, CancellationToken ct = default)
    {
        var items = await repository.GetAllCategoriesAsync(activeOnly, ct);
        return items.Select(c => new CategoryResponse(c.Id, c.Name, c.IsActive, c.CreatedAt)).ToList();
    }

    public async Task<CategoryResponse?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var category = await repository.GetCategoryByIdAsync(id, ct);
        return category is null ? null : new CategoryResponse(category.Id, category.Name, category.IsActive, category.CreatedAt);
    }

    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("O nome da categoria é obrigatório.");

        if (await repository.ExistsCategoryByNameAsync(request.Name.Trim(), null, ct))
            throw new InvalidOperationException($"Já existe uma categoria cadastrada com o nome '{request.Name}'.");

        var category = new Category
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var created = await repository.CreateCategoryAsync(category, ct);
        return new CategoryResponse(created.Id, created.Name, created.IsActive, created.CreatedAt);
    }

    public async Task<CategoryResponse?> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct = default)
    {
        var existing = await repository.GetCategoryByIdAsync(id, ct);
        if (existing is null) return null;

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("O nome da categoria é obrigatório.");

        if (await repository.ExistsCategoryByNameAsync(request.Name.Trim(), id, ct))
            throw new InvalidOperationException($"Já existe uma categoria cadastrada com o nome '{request.Name}'.");

        var wasActive = existing.IsActive;
        existing.Name = request.Name.Trim();
        existing.IsActive = request.IsActive;

        var updated = await repository.UpdateCategoryAsync(existing, ct);

        // Inativação em cascata se inativada
        if (wasActive && !request.IsActive)
        {
            await repository.InactivateCategoryCascadeAsync(id, ct);
        }

        return new CategoryResponse(updated.Id, updated.Name, updated.IsActive, updated.CreatedAt);
    }

    public async Task<bool> InactivateAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await repository.GetCategoryByIdAsync(id, ct);
        if (existing is null) return false;

        existing.IsActive = false;
        await repository.UpdateCategoryAsync(existing, ct);
        await repository.InactivateCategoryCascadeAsync(id, ct);
        return true;
    }
}
