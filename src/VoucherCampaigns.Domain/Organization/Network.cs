namespace VoucherCampaigns.Domain.Organization;

/// <summary>Rede: topo da hierarquia organizacional.</summary>
public class Network
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Region> Regions { get; set; } = [];
}
