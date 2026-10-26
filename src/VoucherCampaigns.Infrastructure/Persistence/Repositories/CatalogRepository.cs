using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Catalog;

namespace VoucherCampaigns.Infrastructure.Persistence.Repositories;

public class CatalogRepository(AppDbContext context) : ICatalogRepository
{
    public async Task<IReadOnlyList<Category>> GetAllCategoriesAsync(bool? activeOnly, CancellationToken cancellationToken)
    {
        var query = ApplyActive(context.Categories.AsNoTracking(), activeOnly);
        return await query.OrderBy(category => category.Name).ToListAsync(cancellationToken);
    }

    public Task<Category?> GetCategoryByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Categories.AsNoTracking().FirstOrDefaultAsync(category => category.Id == id, cancellationToken);

    public async Task<Category> CreateCategoryAsync(Category category, CancellationToken cancellationToken)
    {
        context.Categories.Add(category);
        await ConstraintSave.SaveAsync(context, CatalogErrors.CategoryNameTakenCode, CatalogErrors.CategoryNameTakenMessage, cancellationToken);
        return category;
    }

    public async Task<Category> UpdateCategoryAsync(Category category, CancellationToken cancellationToken)
    {
        IQueryable<Category> query = context.Categories;

        if (!category.IsActive)
        {
            query = query.Include(stored => stored.Products);
        }

        var stored = await query.FirstAsync(stored => stored.Id == category.Id, cancellationToken);
        stored.Name = category.Name;
        stored.IsActive = category.IsActive;

        // Pai e produtos na mesma gravação: a inativação não pode ficar pela metade.
        if (!category.IsActive)
        {
            foreach (var product in stored.Products)
            {
                product.IsActive = false;
            }
        }

        await ConstraintSave.SaveAsync(context, CatalogErrors.CategoryNameTakenCode, CatalogErrors.CategoryNameTakenMessage, cancellationToken);
        return stored;
    }

    public Task<bool> ExistsCategoryByNameAsync(string name, Guid? excludeId, CancellationToken cancellationToken) =>
        context.Categories.AnyAsync(
            category => category.Name == name && (excludeId == null || category.Id != excludeId), cancellationToken);

    public async Task<IReadOnlyList<Product>> GetProductsAsync(Guid? categoryId, bool? activeOnly, CancellationToken cancellationToken)
    {
        var query = ApplyActive(context.Products.AsNoTracking(), activeOnly);

        if (categoryId is not null)
        {
            query = query.Where(product => product.CategoryId == categoryId);
        }

        return await query.OrderBy(product => product.Name).ToListAsync(cancellationToken);
    }

    public Task<Product?> GetProductByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Products.AsNoTracking().FirstOrDefaultAsync(product => product.Id == id, cancellationToken);

    public async Task<Product> CreateProductAsync(Product product, CancellationToken cancellationToken)
    {
        context.Products.Add(product);
        await ConstraintSave.SaveAsync(context, CatalogErrors.ProductCodeTakenCode, CatalogErrors.ProductCodeTakenMessage, cancellationToken);
        return product;
    }

    public async Task<Product> UpdateProductAsync(Product product, CancellationToken cancellationToken)
    {
        var stored = await context.Products.FirstAsync(stored => stored.Id == product.Id, cancellationToken);
        stored.Code = product.Code;
        stored.Name = product.Name;
        stored.IsActive = product.IsActive;
        await ConstraintSave.SaveAsync(context, CatalogErrors.ProductCodeTakenCode, CatalogErrors.ProductCodeTakenMessage, cancellationToken);
        return stored;
    }

    public Task<bool> ExistsProductByCodeAsync(string code, Guid? excludeId, CancellationToken cancellationToken) =>
        context.Products.AnyAsync(product => product.Code == code && (excludeId == null || product.Id != excludeId), cancellationToken);

    private static IQueryable<T> ApplyActive<T>(IQueryable<T> query, bool? activeOnly) where T : class =>
        activeOnly switch
        {
            true => query.Where(entity => EF.Property<bool>(entity, nameof(Category.IsActive))),
            false => query.Where(entity => !EF.Property<bool>(entity, nameof(Category.IsActive))),
            _ => query
        };
}
