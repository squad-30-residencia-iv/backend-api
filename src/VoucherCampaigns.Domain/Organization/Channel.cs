namespace VoucherCampaigns.Domain.Organization;

/// <summary>Canal de venda de um estabelecimento (físico ou virtual).</summary>
public class Channel
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid EstablishmentId { get; set; }

    public Establishment? Establishment { get; set; }

    public ChannelType Type { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
