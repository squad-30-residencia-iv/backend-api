using VoucherCampaigns.Domain.Enums;

namespace VoucherCampaigns.Domain.Entities;

public class Voucher
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public Guid CampaignId { get; set; }
    public VoucherStatus Status { get; set; } = VoucherStatus.Available;
    public DateTime ExpirationDate { get; set; }
    public int UsageLimit { get; set; } = 1;
    public int CurrentUsageCount { get; set; } = 0;

    // Navegação (Assumindo que a classe Campaign já existe no GH-8)
    // public Campaign? Campaign { get; set; } 
    public ICollection<VoucherEvent> Events { get; set; } = new List<VoucherEvent>();
}