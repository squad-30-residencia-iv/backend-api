namespace VoucherCampaigns.Domain.Vouchers;

/// <summary>
/// Compra com o carrinho fechado, já resolvida contra o catálogo: cada item conhece o produto e a categoria
/// quando o código existe no catálogo. O valor total é o bruto informado pelo satélite (PR-16).
/// </summary>
public record Purchase(decimal TotalAmount, IReadOnlyList<PurchaseItem> Items, int? CustomerPurchaseCount)
{
    /// <summary>Volume de itens: a soma das quantidades (US-04.1, "volume de itens").</summary>
    public int ItemCount => Items.Sum(item => item.Quantity);
}

public record PurchaseItem(string ProductCode, int Quantity, decimal UnitPrice, Guid? ProductId, Guid? CategoryId)
{
    public decimal Amount => Quantity * UnitPrice;
}
