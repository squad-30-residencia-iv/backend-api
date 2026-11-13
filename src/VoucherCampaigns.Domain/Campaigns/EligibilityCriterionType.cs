namespace VoucherCampaigns.Domain.Campaigns;

/// <summary>Critérios do público restrito (spec GH-047, D-3): perfil, histórico de compras e clientes específicos.</summary>
public enum EligibilityCriterionType
{
    /// <summary>Faixa etária em anos completos na data da compra (US-06.1, D-11).</summary>
    AgeRange,

    /// <summary>UF ou município (código IBGE) do cliente (US-06.1, seção 1.6).</summary>
    Location,

    /// <summary>Cliente do clube de vantagens (US-01.5 e US-04.2, exemplo do backlog).</summary>
    LoyaltyMember,

    /// <summary>Valor gasto na janela (US-06.2, RN12).</summary>
    TotalSpent,

    /// <summary>Quantidade de compras na janela (US-06.2, RN12).</summary>
    PurchaseFrequency,

    /// <summary>Comprou a categoria na janela (US-06.2, RN12).</summary>
    PurchasedCategory,

    /// <summary>Lista de CPFs (US-06.4 e US-06.5, D-8).</summary>
    SpecificCustomers
}
