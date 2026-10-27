using VoucherCampaigns.Domain.Enums;

namespace VoucherCampaigns.Domain.Entities;

public class EligibilityCriterion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CampaignId { get; set; }
    public CriterionType CriterionType { get; set; }
    public string Value { get; set; } = string.Empty;
}