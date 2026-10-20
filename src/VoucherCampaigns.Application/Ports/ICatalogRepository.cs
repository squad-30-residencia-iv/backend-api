using VoucherCampaigns.Domain.Catalog;

namespace VoucherCampaigns.Application.Ports;

public interface ICatalogRepository
{
    // Categories
    Task<IReadOnlyList<Category>> GetAllCategoriesAsync(bool? activeOnly, CancellationToken ct);
    Task<Category?> GetCategoryByIdAsync(Guid id, CancellationToken ct);
    Task<Category> CreateCategoryAsync(Category category, CancellationToken ct);
    Task<Category> UpdateCategoryAsync(Category category, CancellationToken ct);
    Task<bool> ExistsCategoryByNameAsync(string name, Guid? excludeId, CancellationToken ct);
    Task InactivateCategoryCascadeAsync(Guid categoryId, CancellationToken ct);

    // Products
    Task<IReadOnlyList<Product>> GetProductsAsync(Guid? categoryId, bool? activeOnly, CancellationToken ct);
    Task<Product?> GetProductByIdAsync(Guid id, CancellationToken ct);
    Task<Product> CreateProductAsync(Product product, CancellationToken ct);
    Task<Product> UpdateProductAsync(Product product, CancellationToken ct);
    Task<bool> ExistsProductByCodeAsync(string code, Guid? excludeId, CancellationToken ct);
}
