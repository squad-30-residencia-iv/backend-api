using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Domain.Campaigns;

/// <summary>
/// Onde o voucher pode ser usado (spec GH-070, D-2): uma categoria ou um produto. Com ao menos um alvo de resgate,
/// o resgate exige um item deles na compra e calcula o desconto sobre esses itens, em vez do alvo da concessão.
/// </summary>
public class CampaignRedemptionTarget
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    /// <summary><see cref="TargetType.Category"/> ou <see cref="TargetType.Product"/>; não há alvo "todos" de resgate.</summary>
    public TargetType TargetType { get; set; }

    public Guid? CategoryId { get; set; }

    public Category? Category { get; set; }

    public Guid? ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>O item da compra é o produto ou um produto da categoria.</summary>
    public bool Matches(PurchaseItem item) => TargetType switch
    {
        TargetType.Product => item.ProductId is not null && item.ProductId == ProductId,
        TargetType.Category => item.CategoryId is not null && item.CategoryId == CategoryId,
        _ => false
    };
}
