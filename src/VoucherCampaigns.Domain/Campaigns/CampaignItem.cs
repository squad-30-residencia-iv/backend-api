using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Domain.Vouchers;

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

    /// <summary>Vouchers confirmados deste alvo (RN09a). Mantido pelo motor em comandos condicionais.</summary>
    public int IssuedVouchers { get; set; }

    /// <summary>Vouchers provisórios vivos deste alvo.</summary>
    public int ReservedVouchers { get; set; }

    /// <summary>Cota do alvo (RF06a): sem cota, segue o saldo da campanha.</summary>
    public bool HasQuota => VoucherLimit is null || IssuedVouchers + ReservedVouchers < VoucherLimit;

    /// <summary>O item da compra pertence ao alvo: qualquer item em "todos", o produto, ou um produto da categoria.</summary>
    public bool Matches(PurchaseItem item) => TargetType switch
    {
        TargetType.All => true,
        TargetType.Product => item.ProductId is not null && item.ProductId == ProductId,
        TargetType.Category => item.CategoryId is not null && item.CategoryId == CategoryId,
        _ => false
    };
}
