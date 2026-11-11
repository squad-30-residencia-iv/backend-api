using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Domain.Campaigns;

/// <summary>Condição de geração do voucher (US-01.4); cada tipo usa só o seu parâmetro.</summary>
public class CampaignCondition
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    public ConditionType Type { get; set; }

    /// <summary><see cref="ConditionType.PurchaseTotal"/>: valor mínimo da compra.</summary>
    public decimal? MinAmount { get; set; }

    /// <summary><see cref="ConditionType.ItemQuantity"/>: quantidade mínima de itens.</summary>
    public int? MinQuantity { get; set; }

    public Guid? CategoryId { get; set; }

    public Category? Category { get; set; }

    public Guid? ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>
    /// Avaliação sobre a compra (US-04.1, spec GH-045 D-11). Dado que a condição exige e a compra não traz
    /// (quantidade de compras do cliente) não satisfaz.
    /// </summary>
    public bool IsSatisfiedBy(Purchase purchase) => Type switch
    {
        ConditionType.PurchaseTotal => purchase.TotalAmount >= MinAmount,
        ConditionType.ItemQuantity => purchase.ItemCount >= MinQuantity,
        ConditionType.Category => purchase.Items.Any(item => item.CategoryId is not null && item.CategoryId == CategoryId),
        ConditionType.Product => purchase.Items.Any(item => item.ProductId is not null && item.ProductId == ProductId),
        ConditionType.CustomerPurchaseCount => purchase.CustomerPurchaseCount is { } purchases && purchases >= MinQuantity,
        _ => false
    };
}
