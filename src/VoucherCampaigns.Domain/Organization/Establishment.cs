namespace VoucherCampaigns.Domain.Organization;

/// <summary>Estabelecimento (loja), ligado a uma região.</summary>
public class Establishment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid RegionId { get; set; }

    public Region? Region { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Channel> Channels { get; set; } = [];
}
