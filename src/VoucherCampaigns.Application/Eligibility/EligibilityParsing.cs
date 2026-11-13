using System.Security.Cryptography;
using System.Text;
using VoucherCampaigns.Application.Audit;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Application.Vouchers;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Eligibility;

namespace VoucherCampaigns.Application.Eligibility;

/// <summary>Critérios já validados e a lista de CPFs, normalizada e sem repetição (spec GH-047, D-8).</summary>
public sealed record ParsedAudience(IReadOnlyList<CampaignEligibilityCriterion> Criteria, IReadOnlyList<string> Documents);

/// <summary>Atributos do cliente validados; o histórico ainda com os códigos de produto, resolvidos depois no catálogo.</summary>
public sealed record ParsedProfile(
    string Document,
    DateOnly? BirthDate,
    string? State,
    string? CityCode,
    bool? LoyaltyMember,
    IReadOnlyList<ParsedHistoryWindow> History);

public sealed record ParsedHistoryWindow(int WindowDays, decimal? TotalSpent, int? PurchaseCount, IReadOnlyList<string>? ProductCodes);

/// <summary>Validação do público restrito e dos atributos do cliente (spec GH-047, 2.1); erros de contrato viram 400.</summary>
public static class EligibilityParsing
{
    /// <summary>As 27 UFs (seção 1.6).</summary>
    public static readonly IReadOnlySet<string> States = new HashSet<string>(StringComparer.Ordinal)
    {
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB",
        "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"
    };

    /// <summary>
    /// Critérios da <c>PUT /audience</c>. Os ids saem em sequência a partir de <paramref name="idBase"/>, para os
    /// critérios voltarem na ordem informada. A existência da categoria é conferida depois, no catálogo.
    /// </summary>
    public static ParsedAudience ParseCriteria(
        CampaignAudienceType audienceType, IReadOnlyList<EligibilityCriterionRequest?>? requests, DateTimeOffset idBase)
    {
        var informed = requests ?? [];

        if (audienceType == CampaignAudienceType.AllCustomers)
        {
            return informed.Count == 0
                ? new ParsedAudience([], [])
                : throw new RequestValidationException("Criteria", EligibilityContract.CriteriaNotAllowed);
        }

        if (informed.Count == 0)
        {
            throw new RequestValidationException("Criteria", EligibilityContract.CriteriaRequired);
        }

        var criteria = new List<CampaignEligibilityCriterion>();
        IReadOnlyList<string> documents = [];

        for (var index = 0; index < informed.Count; index++)
        {
            var field = $"Criteria[{index}]";
            var request = informed[index] ?? throw new RequestValidationException($"{field}.Type", EligibilityContract.CriterionTypeRequired);
            var criterion = ParseCriterion(request, field, out var parsedDocuments);
            criterion.Id = Guid.CreateVersion7(idBase.AddMilliseconds(index));

            // Um critério de cada tipo; a categoria pode repetir com categorias diferentes (D-3).
            if (criteria.Any(existing => existing.Type == criterion.Type
                && (criterion.Type != EligibilityCriterionType.PurchasedCategory
                    || (existing.CategoryId == criterion.CategoryId && existing.WindowDays == criterion.WindowDays))))
            {
                throw new RequestValidationException($"{field}.Type", EligibilityContract.CriterionDuplicated);
            }

            if (parsedDocuments is not null)
            {
                documents = parsedDocuments;
            }

            criteria.Add(criterion);
        }

        return new ParsedAudience(criteria, documents);
    }

    /// <summary>
    /// Atributos do cliente na concessão e na simulação (D-2). A data de nascimento futura é recusada com a maior folga
    /// de fuso (UTC+14): a data local da rede nunca passa disso.
    /// </summary>
    public static ParsedProfile ParseProfile(ConcessionCustomerRequest customer, string document, DateTime utcNow, string field)
    {
        if (customer.BirthDate is { } birthDate && birthDate > DateOnly.FromDateTime(utcNow.AddHours(14)))
        {
            throw new RequestValidationException($"{field}.BirthDate", EligibilityContract.BirthDateInFuture);
        }

        string? state = null;

        if (customer.State is { } informedState)
        {
            state = informedState.Trim().ToUpperInvariant();

            if (!States.Contains(state))
            {
                throw new RequestValidationException($"{field}.State", EligibilityContract.CustomerStateInvalid);
            }
        }

        string? cityCode = null;

        if (customer.CityCode is { } informedCity)
        {
            cityCode = informedCity.Trim();

            if (!IsCityCode(cityCode))
            {
                throw new RequestValidationException($"{field}.CityCode", EligibilityContract.CustomerCityCodeInvalid);
            }
        }

        var history = new List<ParsedHistoryWindow>();

        for (var index = 0; index < (customer.History?.Count ?? 0); index++)
        {
            var windowField = $"{field}.History[{index}]";
            var window = customer.History![index]
                ?? throw new RequestValidationException($"{windowField}.WindowDays", EligibilityContract.HistoryWindowRequired);
            var days = window.WindowDays!.Value;

            if (history.Any(existing => existing.WindowDays == days))
            {
                throw new RequestValidationException($"{windowField}.WindowDays", EligibilityContract.HistoryDuplicated);
            }

            if (window.TotalSpent is { } spent && (spent < 0 || decimal.Round(spent, 2) != spent || spent > ConcessionContract.MaxAmount))
            {
                throw new RequestValidationException($"{windowField}.TotalSpent", EligibilityContract.HistoryTotalSpentInvalid);
            }

            List<string>? products = null;

            if (window.ProductCodes is { } codes)
            {
                products = [];

                foreach (var code in codes)
                {
                    var trimmed = code?.Trim();

                    if (string.IsNullOrEmpty(trimmed) || trimmed.Length > ConcessionContract.MaxProductCodeLength)
                    {
                        throw new RequestValidationException($"{windowField}.ProductCodes", EligibilityContract.HistoryProductCodeInvalid);
                    }

                    products.Add(trimmed);
                }
            }

            history.Add(new ParsedHistoryWindow(days, window.TotalSpent, window.PurchaseCount, products));
        }

        return new ParsedProfile(document, customer.BirthDate, state, cityCode, customer.LoyaltyMember, history);
    }

    /// <summary>Resolve os produtos do histórico nas categorias do catálogo; código desconhecido é ignorado.</summary>
    public static async Task<CustomerProfile> ResolveAsync(ICatalogRepository catalog, ParsedProfile profile, CancellationToken cancellationToken)
    {
        // Mesma comparação do item da compra: o código do produto não diferencia maiúsculas.
        var codes = profile.History.SelectMany(window => window.ProductCodes ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var categoryOf = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        if (codes.Count > 0)
        {
            foreach (var product in await catalog.GetProductsByCodesAsync(codes, cancellationToken))
            {
                categoryOf.TryAdd(product.Code, product.CategoryId);
            }
        }

        var history = profile.History
            .Select(window => new CustomerHistoryWindow(
                window.WindowDays,
                window.TotalSpent,
                window.PurchaseCount,
                window.ProductCodes?.Where(categoryOf.ContainsKey).Select(code => categoryOf[code]).Distinct().ToList()))
            .ToList();

        return new CustomerProfile(profile.Document, profile.BirthDate, profile.State, profile.CityCode, profile.LoyaltyMember, history);
    }

    /// <summary>Snapshot gravado na concessão e nos eventos (D-12).</summary>
    public static object Summary(EligibilityResult result) => new
    {
        result.Eligible,
        Criteria = result.Criteria.Select(criterion => new
        {
            Type = criterion.Type.ToString(),
            criterion.Parameters,
            criterion.Observed,
            Outcome = criterion.Outcome.ToString()
        })
    };

    public static string Snapshot(EligibilityResult result) => AuditJson.Serialize(Summary(result));

    public static IReadOnlyList<CriterionResultResponse> MapResults(EligibilityResult result) =>
        result.Criteria
            .Select(criterion => new CriterionResultResponse(criterion.Type.ToString(), criterion.Outcome.ToString(), criterion.Parameters, criterion.Observed))
            .ToList();

    /// <summary>SHA-256 em hexadecimal da lista ordenada (D-20).</summary>
    public static string Digest(IEnumerable<string> documents)
    {
        var joined = string.Join(',', documents.Order(StringComparer.Ordinal));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
    }

    private static bool IsCityCode(string value) => value.Length == EligibilityContract.CityCodeLength && value.All(char.IsAsciiDigit);

    private static CampaignEligibilityCriterion ParseCriterion(EligibilityCriterionRequest request, string field, out IReadOnlyList<string>? documents)
    {
        documents = null;

        var trimmed = request.Type?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new RequestValidationException($"{field}.Type", EligibilityContract.CriterionTypeRequired);
        }

        if (!Enum.GetNames<EligibilityCriterionType>().Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            throw new RequestValidationException($"{field}.Type", EligibilityContract.CriterionTypeInvalid);
        }

        var type = Enum.Parse<EligibilityCriterionType>(trimmed, ignoreCase: true);
        var criterion = new CampaignEligibilityCriterion { Type = type };

        switch (type)
        {
            case EligibilityCriterionType.AgeRange:
                EnsureOnly(request, field, nameof(request.MinAge), nameof(request.MaxAge));

                if (request.MinAge is null && request.MaxAge is null)
                {
                    throw new RequestValidationException($"{field}.MinAge", EligibilityContract.AgeRequired);
                }

                EnsureAge(request.MinAge, $"{field}.MinAge");
                EnsureAge(request.MaxAge, $"{field}.MaxAge");

                if (request.MinAge > request.MaxAge)
                {
                    throw new RequestValidationException($"{field}.MinAge", EligibilityContract.AgeRangeInverted);
                }

                criterion.MinAge = request.MinAge;
                criterion.MaxAge = request.MaxAge;
                break;

            case EligibilityCriterionType.Location:
                EnsureOnly(request, field, nameof(request.States), nameof(request.CityCodes));
                criterion.States = ParseList(request.States, $"{field}.States", value => value.ToUpperInvariant(), States.Contains, EligibilityContract.StateInvalid);
                criterion.CityCodes = ParseList(request.CityCodes, $"{field}.CityCodes", value => value, IsCityCode, EligibilityContract.CityCodeInvalid);

                if (criterion.States.Count == 0 && criterion.CityCodes.Count == 0)
                {
                    throw new RequestValidationException($"{field}.States", EligibilityContract.LocationRequired);
                }

                break;

            case EligibilityCriterionType.LoyaltyMember:
                EnsureOnly(request, field);
                break;

            case EligibilityCriterionType.TotalSpent:
                EnsureOnly(request, field, nameof(request.MinAmount), nameof(request.WindowDays));
                var amount = request.MinAmount ?? throw new RequestValidationException($"{field}.MinAmount", EligibilityContract.MinAmountRequired);

                if (amount <= 0 || decimal.Round(amount, 2) != amount || amount > ConcessionContract.MaxAmount)
                {
                    throw new RequestValidationException($"{field}.MinAmount", EligibilityContract.MinAmountPositive);
                }

                criterion.MinAmount = amount;
                criterion.WindowDays = ParseWindow(request.WindowDays, field);
                break;

            case EligibilityCriterionType.PurchaseFrequency:
                EnsureOnly(request, field, nameof(request.MinPurchases), nameof(request.WindowDays));
                var purchases = request.MinPurchases ?? throw new RequestValidationException($"{field}.MinPurchases", EligibilityContract.MinPurchasesRequired);

                if (purchases <= 0)
                {
                    throw new RequestValidationException($"{field}.MinPurchases", EligibilityContract.MinPurchasesPositive);
                }

                criterion.MinPurchases = purchases;
                criterion.WindowDays = ParseWindow(request.WindowDays, field);
                break;

            case EligibilityCriterionType.PurchasedCategory:
                EnsureOnly(request, field, nameof(request.CategoryId), nameof(request.WindowDays));
                criterion.CategoryId = request.CategoryId is { } categoryId && categoryId != Guid.Empty
                    ? categoryId
                    : throw new RequestValidationException($"{field}.CategoryId", EligibilityContract.CategoryRequired);
                criterion.WindowDays = ParseWindow(request.WindowDays, field);
                break;

            case EligibilityCriterionType.SpecificCustomers:
                EnsureOnly(request, field, nameof(request.Documents));
                documents = ParseDocuments(request.Documents, $"{field}.Documents");
                criterion.CustomerCount = documents.Count;
                criterion.CustomersDigest = Digest(documents);
                break;
        }

        return criterion;
    }

    /// <summary>Todos os inválidos de uma vez, com a posição (a partir de 1), e nada gravado (US-06.5 cenário 3, D-8).</summary>
    private static List<string> ParseDocuments(IReadOnlyList<string?>? values, string field)
    {
        if (values is null || values.Count == 0)
        {
            throw new RequestValidationException(field, EligibilityContract.DocumentsRequired);
        }

        if (values.Count > EligibilityContract.MaxDocuments)
        {
            throw new RequestValidationException(field, EligibilityContract.DocumentsTooMany);
        }

        var documents = new List<string>();
        var invalid = new List<int>();

        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index] ?? string.Empty;
            var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
            var others = value.Count(character => !char.IsAsciiDigit(character) && character is not ('.' or '-' or ' '));

            if (others > 0 || !Cpf.IsValid(digits))
            {
                invalid.Add(index + 1);
                continue;
            }

            documents.Add(digits);
        }

        if (invalid.Count > 0)
        {
            throw new RequestValidationException(field, string.Format(EligibilityContract.DocumentsInvalid, string.Join(", ", invalid)));
        }

        return documents.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    private static List<string> ParseList(
        IReadOnlyList<string?>? values, string field, Func<string, string> normalize, Func<string, bool> isValid, string invalidMessage)
    {
        if (values is null)
        {
            return [];
        }

        if (values.Count > EligibilityContract.MaxLocationEntries)
        {
            throw new RequestValidationException(field, EligibilityContract.LocationTooMany);
        }

        var parsed = new List<string>();

        foreach (var value in values)
        {
            var normalized = normalize(value?.Trim() ?? string.Empty);

            if (!isValid(normalized))
            {
                throw new RequestValidationException(field, invalidMessage);
            }

            parsed.Add(normalized);
        }

        return parsed.Distinct(StringComparer.Ordinal).ToList();
    }

    private static void EnsureAge(int? age, string field)
    {
        if (age is < 0 or > EligibilityContract.MaxAge)
        {
            throw new RequestValidationException(field, EligibilityContract.AgeRange);
        }
    }

    private static int ParseWindow(int? value, string field)
    {
        var days = value ?? throw new RequestValidationException($"{field}.WindowDays", EligibilityContract.HistoryWindowRequired);

        return days is >= 1 and <= EligibilityContract.MaxWindowDays
            ? days
            : throw new RequestValidationException($"{field}.WindowDays", EligibilityContract.HistoryWindowRange);
    }

    /// <summary>Recusa os parâmetros informados que não são do tipo (como nas condições, US-01.4 D-8).</summary>
    private static void EnsureOnly(EligibilityCriterionRequest request, string field, params string[] allowed)
    {
        var informed = new (string Name, bool HasValue)[]
        {
            (nameof(request.MinAge), request.MinAge is not null),
            (nameof(request.MaxAge), request.MaxAge is not null),
            (nameof(request.States), request.States is not null),
            (nameof(request.CityCodes), request.CityCodes is not null),
            (nameof(request.MinAmount), request.MinAmount is not null),
            (nameof(request.MinPurchases), request.MinPurchases is not null),
            (nameof(request.CategoryId), request.CategoryId is not null),
            (nameof(request.WindowDays), request.WindowDays is not null),
            (nameof(request.Documents), request.Documents is not null)
        };

        foreach (var (name, hasValue) in informed)
        {
            if (hasValue && !allowed.Contains(name))
            {
                throw new RequestValidationException($"{field}.{name}", EligibilityContract.UnexpectedField);
            }
        }
    }
}
