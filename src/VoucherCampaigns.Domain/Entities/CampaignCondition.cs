using VoucherCampaigns.Domain.Enums;

namespace VoucherCampaigns.Domain.Entities;

public class CampaignCondition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CampaignId { get; set; }
    public ConditionType ConditionType { get; set; }
    public string Value { get; set; } = string.Empty;
    public ConditionOperator Operator { get; set; }
}