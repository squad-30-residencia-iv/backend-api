using VoucherCampaigns.Domain.Catalog;

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
}
