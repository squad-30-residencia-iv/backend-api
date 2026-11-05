namespace VoucherCampaigns.Domain.Campaigns;

/// <summary>Condições de geração do voucher (RF14a, US-01.4).</summary>
public enum ConditionType
{
    /// <summary>Valor total da compra maior ou igual ao mínimo.</summary>
    PurchaseTotal,

    /// <summary>Volume de itens da compra maior ou igual ao mínimo.</summary>
    ItemQuantity,

    /// <summary>Ao menos um item da categoria.</summary>
    Category,

    /// <summary>Ao menos um item do produto.</summary>
    Product,

    /// <summary>Quantidade de compras do cliente; bloqueada pela decisão D2.</summary>
    CustomerPurchaseCount,

    /// <summary>Combinação de produtos; semântica ainda não definida no backlog.</summary>
    ProductCombination,

    /// <summary>Período da compra; semântica ainda não definida no backlog.</summary>
    PurchasePeriod
}
