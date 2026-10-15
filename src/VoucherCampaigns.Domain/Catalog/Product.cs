namespace VoucherCampaigns.Domain.Catalog;

public class Product
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CategoryId { get; set; }

    public Category? Category { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
