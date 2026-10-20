using VoucherCampaigns.Application.DTOs.Catalog;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Catalog;

namespace VoucherCampaigns.Application.Catalog;

public interface IProductService
{
    Task<IReadOnlyList<ProductResponse>> GetAllAsync(Guid? categoryId, bool? activeOnly, CancellationToken ct = default);
    Task<ProductResponse?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken ct = default);
    Task<ProductResponse?> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken ct = default);
    Task<bool> InactivateAsync(Guid id, CancellationToken ct = default);
}

public class ProductService(ICatalogRepository repository) : IProductService
{
    public async Task<IReadOnlyList<ProductResponse>> GetAllAsync(Guid? categoryId, bool? activeOnly, CancellationToken ct = default)
    {
        var items = await repository.GetProductsAsync(categoryId, activeOnly, ct);
        return items.Select(p => new ProductResponse(p.Id, p.CategoryId, p.Code, p.Name, p.IsActive, p.CreatedAt)).ToList();
    }

    public async Task<ProductResponse?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var product = await repository.GetProductByIdAsync(id, ct);
        return product is null
            ? null
            : new ProductResponse(product.Id, product.CategoryId, product.Code, product.Name, product.IsActive, product.CreatedAt);
    }

    public async Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new ArgumentException("O código do produto é obrigatório.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("O nome do produto é obrigatório.");

        // Validação de integridade: a categoria pai deve existir e estar ativa
        var parentCategory = await repository.GetCategoryByIdAsync(request.CategoryId, ct);
        if (parentCategory is null || !parentCategory.IsActive)
            throw new InvalidOperationException("A categoria informada não existe ou está inativa.");

        if (await repository.ExistsProductByCodeAsync(request.Code.Trim(), null, ct))
            throw new InvalidOperationException($"Já existe um produto cadastrado com o código '{request.Code}'.");

        var product = new Product
        {
            Id = Guid.CreateVersion7(),
            CategoryId = request.CategoryId,
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var created = await repository.CreateProductAsync(product, ct);
        return new ProductResponse(created.Id, created.CategoryId, created.Code, created.Name, created.IsActive, created.CreatedAt);
    }

    public async Task<ProductResponse?> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken ct = default)
    {
        var existing = await repository.GetProductByIdAsync(id, ct);
        if (existing is null) return null;

        if (string.IsNullOrWhiteSpace(request.Code))
            throw new ArgumentException("O código do produto é obrigatório.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("O nome do produto é obrigatório.");

        if (await repository.ExistsProductByCodeAsync(request.Code.Trim(), id, ct))
            throw new InvalidOperationException($"Já existe um produto cadastrado com o código '{request.Code}'.");

        existing.Code = request.Code.Trim();
        existing.Name = request.Name.Trim();
        existing.IsActive = request.IsActive;

        var updated = await repository.UpdateProductAsync(existing, ct);
        return new ProductResponse(updated.Id, updated.CategoryId, updated.Code, updated.Name, updated.IsActive, updated.CreatedAt);
    }

    public async Task<bool> InactivateAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await repository.GetProductByIdAsync(id, ct);
        if (existing is null) return false;

        existing.IsActive = false;
        await repository.UpdateProductAsync(existing, ct);
        return true;
    }
}
