using VoucherCampaigns.Domain.Catalog;

namespace VoucherCampaigns.Application.Ports;

public interface ICatalogRepository
{
    Task<IReadOnlyList<Category>> GetAllCategoriesAsync(bool? activeOnly, CancellationToken cancellationToken);

    Task<Category?> GetCategoryByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Category> CreateCategoryAsync(Category category, CancellationToken cancellationToken);

    Task<Category> UpdateCategoryAsync(Category category, CancellationToken cancellationToken);

    Task<bool> ExistsCategoryByNameAsync(string name, Guid? excludeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Product>> GetProductsAsync(Guid? categoryId, bool? activeOnly, CancellationToken cancellationToken);

    Task<Product?> GetProductByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Product> CreateProductAsync(Product product, CancellationToken cancellationToken);

    Task<Product> UpdateProductAsync(Product product, CancellationToken cancellationToken);

    Task<bool> ExistsProductByCodeAsync(string code, Guid? excludeId, CancellationToken cancellationToken);
}
