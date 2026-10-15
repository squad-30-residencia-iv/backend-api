using VoucherCampaigns.Domain.Catalog;

namespace VoucherCampaigns.Domain.Campaigns;

/// <summary>Item de campanha: alvo, benefício e quantidade de vouchers.</summary>
public class CampaignItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    public TargetType TargetType { get; set; }

    public Guid? CategoryId { get; set; }

    public Category? Category { get; set; }

    public Guid? ProductId { get; set; }

    public Product? Product { get; set; }

    public BenefitType BenefitType { get; set; }

    public decimal BenefitValue { get; set; }

    public int? VoucherLimit { get; set; }
}
