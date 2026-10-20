namespace VoucherCampaigns.Application.DTOs.Catalog;

public record CreateCategoryRequest(string Name);

public record UpdateCategoryRequest(string Name, bool IsActive);

public record CategoryResponse(Guid Id, string Name, bool IsActive, DateTime CreatedAt);

public record CreateProductRequest(Guid CategoryId, string Code, string Name);

public record UpdateProductRequest(string Code, string Name, bool IsActive);

public record ProductResponse(Guid Id, Guid CategoryId, string Code, string Name, bool IsActive, DateTime CreatedAt);
