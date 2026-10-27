using VoucherCampaigns.Domain.Enums;

namespace VoucherCampaigns.Domain.Entities;

public class VoucherEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VoucherId { get; set; }
    public VoucherEventType EventType { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Details { get; set; } = string.Empty;

    public Voucher? Voucher { get; set; }
}