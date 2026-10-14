namespace VoucherCampaigns.Domain.Organization;

public class Region
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid NetworkId { get; set; }

    public Network? Network { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Establishment> Establishments { get; set; } = [];
}
