using VoucherCampaigns.Application.Vouchers;
using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Application.Eligibility;

/// <summary>Catálogo dos critérios do público restrito para o portal montar o formulário (spec GH-075, D-2).</summary>
/// <param name="MaxCriteria">Quantos critérios a campanha aceita, somando todos os tipos.</param>
public record EligibilityCatalogResponse(int MaxCriteria, IReadOnlyList<EligibilityCriterionCatalogResponse> Criteria);

/// <param name="Type">Valor de <c>type</c> no <c>PUT /campaigns/{id}/audience</c>.</param>
/// <param name="RequiresHistoryWindow">Pede <c>windowDays</c>, e o satélite precisa mandar o histórico dessa janela.</param>
/// <param name="SatelliteAttributes">Atributos de <c>customer</c> que o satélite manda na concessão (os nomes de <c>requiredAttributes</c>).</param>
public record EligibilityCriterionCatalogResponse(
    string Type,
    string Label,
    string Description,
    IReadOnlyList<EligibilityFieldCatalogResponse> Fields,
    bool RequiresHistoryWindow,
    IReadOnlyList<string> SatelliteAttributes);

/// <param name="Kind"><c>integer</c>, <c>decimal</c>, <c>stateList</c>, <c>cityCodeList</c>, <c>categoryId</c> ou <c>documentList</c>.</param>
public record EligibilityFieldCatalogResponse(
    string Name,
    string Kind,
    bool Required,
    decimal? Min,
    decimal? Max,
    int? MaxItems,
    string? Note);

/// <summary>
/// Atributos do cliente que cada critério pede ao satélite. Um lugar só para o catálogo (GH-075) e para os
/// <c>requiredAttributes</c> de <c>GET /satellite/campaigns</c> (GH-047, D-15).
/// </summary>
public static class EligibilityAttributes
{
    public const string BirthDate = "birthDate";
    public const string State = "state";
    public const string CityCode = "cityCode";
    public const string LoyaltyMember = "loyaltyMember";
    public const string HistoryTotalSpent = "history.totalSpent";
    public const string HistoryPurchaseCount = "history.purchaseCount";
    public const string HistoryProductCodes = "history.productCodes";

    /// <summary>Ordem fixa da resposta do satélite.</summary>
    public static readonly IReadOnlyList<string> Order =
        [BirthDate, State, CityCode, LoyaltyMember, HistoryTotalSpent, HistoryPurchaseCount, HistoryProductCodes];

    /// <summary>Tudo o que o tipo pode pedir (o critério de localização pede só o que tiver: UF, município ou os dois).</summary>
    public static IReadOnlyList<string> ForType(EligibilityCriterionType type) => type switch
    {
        EligibilityCriterionType.AgeRange => [BirthDate],
        EligibilityCriterionType.Location => [State, CityCode],
        EligibilityCriterionType.LoyaltyMember => [LoyaltyMember],
        EligibilityCriterionType.TotalSpent => [HistoryTotalSpent],
        EligibilityCriterionType.PurchaseFrequency => [HistoryPurchaseCount],
        EligibilityCriterionType.PurchasedCategory => [HistoryProductCodes],
        EligibilityCriterionType.SpecificCustomers => [],
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    /// <summary>O que um critério gravado pede ao satélite.</summary>
    public static IReadOnlyList<string> For(CampaignEligibilityCriterion criterion)
    {
        if (criterion.Type != EligibilityCriterionType.Location)
        {
            return ForType(criterion.Type);
        }

        var attributes = new List<string>();

        if (criterion.States.Count > 0)
        {
            attributes.Add(State);
        }

        if (criterion.CityCodes.Count > 0)
        {
            attributes.Add(CityCode);
        }

        return attributes;
    }

    /// <summary>União dos atributos dos critérios, sem repetição e na <see cref="Order"/>.</summary>
    public static List<string> Required(IEnumerable<CampaignEligibilityCriterion> criteria)
    {
        var wanted = criteria.SelectMany(For).ToHashSet(StringComparer.Ordinal);

        return Order.Where(wanted.Contains).ToList();
    }
}

/// <summary>O catálogo é estático: os limites vêm das mesmas constantes que a validação usa (<see cref="EligibilityContract"/>).</summary>
public static class EligibilityCatalog
{
    private const string WindowNote = "Período, em dias, do histórico de compras considerado.";

    private static readonly EligibilityFieldCatalogResponse Window =
        new("windowDays", "integer", true, 1, EligibilityContract.MaxWindowDays, null, WindowNote);

    public static EligibilityCatalogResponse Build() =>
        new(EligibilityContract.MaxCriteria, Enum.GetValues<EligibilityCriterionType>().Select(Describe).ToList());

    private static EligibilityCriterionCatalogResponse Describe(EligibilityCriterionType type)
    {
        (string Label, string Description, List<EligibilityFieldCatalogResponse> Fields) info = type switch
        {
            EligibilityCriterionType.AgeRange => (
                "Faixa etária",
                "Idade em anos completos na data da compra.",
                [
                    new("minAge", "integer", false, 0, EligibilityContract.MaxAge, null, "Informe a idade mínima, a máxima ou as duas."),
                    new("maxAge", "integer", false, 0, EligibilityContract.MaxAge, null, "Não pode ser menor que a idade mínima.")
                ]),
            EligibilityCriterionType.Location => (
                "Localização",
                "UF ou município (código IBGE) do cliente.",
                [
                    new("states", "stateList", false, null, null, EligibilityContract.MaxLocationEntries, "Sigla da UF, como SP. Informe ao menos uma UF ou um município."),
                    new("cityCodes", "cityCodeList", false, null, null, EligibilityContract.MaxLocationEntries, "Código IBGE com 7 dígitos.")
                ]),
            EligibilityCriterionType.LoyaltyMember => (
                "Clube de vantagens",
                "Cliente participante do clube de vantagens.",
                []),
            EligibilityCriterionType.TotalSpent => (
                "Valor gasto",
                "Valor mínimo gasto no período.",
                [
                    new("minAmount", "decimal", true, 0.01m, ConcessionContract.MaxAmount, null, "Maior que zero, com até 2 casas decimais."),
                    Window
                ]),
            EligibilityCriterionType.PurchaseFrequency => (
                "Frequência de compras",
                "Quantidade mínima de compras no período.",
                [
                    new("minPurchases", "integer", true, 1, null, null, null),
                    Window
                ]),
            EligibilityCriterionType.PurchasedCategory => (
                "Comprou a categoria",
                "Comprou ao menos um produto da categoria no período.",
                [
                    new("categoryId", "categoryId", true, null, null, null, "Categoria do catálogo; vale também a categoria inativa (compras passadas)."),
                    Window
                ]),
            EligibilityCriterionType.SpecificCustomers => (
                "Clientes específicos",
                "Lista de CPFs; o cliente precisa estar na lista.",
                [
                    new("documents", "documentList", true, null, null, EligibilityContract.MaxDocuments, "CPFs com ou sem máscara; os inválidos voltam com a posição na lista.")
                ]),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };

        return new EligibilityCriterionCatalogResponse(
            type.ToString(),
            info.Label,
            info.Description,
            info.Fields,
            info.Fields.Any(field => field.Name == Window.Name),
            EligibilityAttributes.ForType(type));
    }
}
