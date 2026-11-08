namespace VoucherCampaigns.Domain.Campaigns;

/// <summary>Transição de status da campanha, gravada no mesmo momento da decisão (US-01.6, US-01.11).</summary>
public class CampaignStatusEvent
{
    public Guid Id { get; set; }

    public Guid CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    public CampaignStatus FromStatus { get; set; }

    public CampaignStatus ToStatus { get; set; }

    public Guid ActorUserId { get; set; }

    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; }
}
