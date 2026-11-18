using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Domain.Eligibility;

/// <summary>Atributos do cliente enviados pelo satélite na própria requisição (PR-08, RF28b, spec GH-047 D-2).</summary>
public sealed record CustomerProfile(
    string Document,
    DateOnly? BirthDate,
    string? State,
    string? CityCode,
    bool? LoyaltyMember,
    IReadOnlyList<CustomerHistoryWindow> History)
{
    public static CustomerProfile OnlyDocument(string document) => new(document, null, null, null, null, []);
}

/// <summary>Histórico já somado pelo satélite numa janela (D-4). <c>CategoryIds</c> vêm dos produtos comprados, resolvidos no catálogo.</summary>
public sealed record CustomerHistoryWindow(int WindowDays, decimal? TotalSpent, int? PurchaseCount, IReadOnlyList<Guid>? CategoryIds);

public enum CriterionOutcome
{
    Met,
    NotMet,

    /// <summary>O atributo não veio na requisição: o critério não é atendido (D-5).</summary>
    Missing
}

/// <summary>Resultado de um critério com os parâmetros e o valor observado (RN11a, D-12).</summary>
public sealed record CriterionResult(EligibilityCriterionType Type, object Parameters, object? Observed, CriterionOutcome Outcome);

public sealed record EligibilityResult(bool Eligible, IReadOnlyList<CriterionResult> Criteria)
{
    /// <summary>Primeiro critério não atendido, para a auditoria da recusa (US-06.3 cenário 2).</summary>
    public CriterionResult? FirstFailed => Criteria.FirstOrDefault(criterion => criterion.Outcome != CriterionOutcome.Met);
}

/// <summary>Avaliação do público restrito, sem I/O (spec GH-047, 1.3). Todos os critérios valem juntos (D-6).</summary>
public static class EligibilityEvaluator
{
    /// <param name="purchaseDate">Data da compra no fuso da rede, para a idade (D-11) .</param>
    /// <param name="isListed">CPF presente na lista de clientes específicos da campanha.</param>
    public static EligibilityResult Evaluate(
        IEnumerable<CampaignEligibilityCriterion> criteria, CustomerProfile customer, DateOnly purchaseDate, bool isListed)
    {
        var results = criteria
            .OrderBy(criterion => criterion.Id)
            .Select(criterion => EvaluateOne(criterion, customer, purchaseDate, isListed))
            .ToList();

        return new EligibilityResult(results.All(result => result.Outcome == CriterionOutcome.Met), results);
    }

    /// <summary>Anos completos na data informada; o aniversário conta no próprio dia.</summary>
    public static int AgeOn(DateOnly birthDate, DateOnly date)
    {
        var age = date.Year - birthDate.Year;
        return birthDate.AddYears(age) > date ? age - 1 : age;
    }

    private static CriterionResult EvaluateOne(
        CampaignEligibilityCriterion criterion, CustomerProfile customer, DateOnly purchaseDate, bool isListed)
    {
        switch (criterion.Type)
        {
            case EligibilityCriterionType.AgeRange:
            {
                var parameters = new { criterion.MinAge, criterion.MaxAge };

                if (customer.BirthDate is not { } birthDate)
                {
                    return Missing(criterion, parameters);
                }

                var age = AgeOn(birthDate, purchaseDate);
                var met = (criterion.MinAge is null || age >= criterion.MinAge) && (criterion.MaxAge is null || age <= criterion.MaxAge);
                return Result(criterion, parameters, new { Age = age }, met);
            }

            case EligibilityCriterionType.Location:
            {
                var parameters = new { criterion.States, criterion.CityCodes };
                var state = criterion.States.Count > 0 ? customer.State : null;
                var city = criterion.CityCodes.Count > 0 ? customer.CityCode : null;

                if (state is null && city is null)
                {
                    return Missing(criterion, parameters);
                }

                var met = (state is not null && criterion.States.Contains(state)) || (city is not null && criterion.CityCodes.Contains(city));
                return Result(criterion, parameters, new { State = state, CityCode = city }, met);
            }

            case EligibilityCriterionType.LoyaltyMember:
            {
                var parameters = new { };

                return customer.LoyaltyMember is { } member
                    ? Result(criterion, parameters, new { LoyaltyMember = member }, member)
                    : Missing(criterion, parameters);
            }

            case EligibilityCriterionType.TotalSpent:
            {
                var parameters = new { criterion.MinAmount, criterion.WindowDays };
                var spent = Window(customer, criterion)?.TotalSpent;

                return spent is { } amount
                    ? Result(criterion, parameters, new { TotalSpent = amount }, amount >= criterion.MinAmount)
                    : Missing(criterion, parameters);
            }

            case EligibilityCriterionType.PurchaseFrequency:
            {
                var parameters = new { criterion.MinPurchases, criterion.WindowDays };
                var count = Window(customer, criterion)?.PurchaseCount;

                return count is { } purchases
                    ? Result(criterion, parameters, new { PurchaseCount = purchases }, purchases >= criterion.MinPurchases)
                    : Missing(criterion, parameters);
            }

            case EligibilityCriterionType.PurchasedCategory:
            {
                var parameters = new { criterion.CategoryId, criterion.WindowDays };
                var categories = Window(customer, criterion)?.CategoryIds;

                if (categories is null)
                {
                    return Missing(criterion, parameters);
                }

                var met = categories.Contains(criterion.CategoryId!.Value);
                return Result(criterion, parameters, new { PurchasedCategory = met }, met);
            }

            case EligibilityCriterionType.SpecificCustomers:
                // Só o resultado: a lista nunca vai para o snapshot (D-12).
                return Result(criterion, new { criterion.CustomerCount }, new { Listed = isListed }, isListed);

            default:
                throw new InvalidOperationException($"Critério sem avaliação: {criterion.Type}.");
        }
    }

    private static CustomerHistoryWindow? Window(CustomerProfile customer, CampaignEligibilityCriterion criterion) =>
        customer.History.FirstOrDefault(window => window.WindowDays == criterion.WindowDays);

    private static CriterionResult Result(CampaignEligibilityCriterion criterion, object parameters, object observed, bool met) =>
        new(criterion.Type, parameters, observed, met ? CriterionOutcome.Met : CriterionOutcome.NotMet);

    private static CriterionResult Missing(CampaignEligibilityCriterion criterion, object parameters) =>
        new(criterion.Type, parameters, null, CriterionOutcome.Missing);
}
