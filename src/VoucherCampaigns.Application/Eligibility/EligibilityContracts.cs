using System.ComponentModel.DataAnnotations;
using VoucherCampaigns.Application.Vouchers;

namespace VoucherCampaigns.Application.Eligibility;

/// <summary>Limites e mensagens do público restrito (spec GH-047, 2.1 e 2.2).</summary>
public static class EligibilityContract
{
    public const int MaxCriteria = 20;
    public const int MaxAge = 130;
    public const int MaxWindowDays = 365;
    public const int MaxLocationEntries = 100;
    public const int MaxDocuments = 10_000;
    public const int MaxHistoryWindows = 10;
    public const int MaxHistoryProducts = 500;
    public const int CityCodeLength = 7;
    public const int MaxCustomersPageSize = 500;
    public const int DefaultCustomersPageSize = 100;

    public const string CriteriaRequired = "Informe ao menos um critério para o público restrito.";
    public const string CriteriaNotAllowed = "O público 'todos os clientes' não aceita critérios.";
    public const string CriteriaTooMany = "A campanha pode ter no máximo 20 critérios de público.";
    public const string CriterionTypeRequired = "Informe o tipo do critério.";
    public const string CriterionTypeInvalid =
        "Critério inválido. Use AgeRange, Location, LoyaltyMember, TotalSpent, PurchaseFrequency, PurchasedCategory ou SpecificCustomers.";
    public const string CriterionDuplicated = "Este critério já foi informado para a campanha.";
    public const string UnexpectedField = "Este parâmetro não se aplica ao critério informado.";

    public const string AgeRequired = "Informe a idade mínima, a máxima ou as duas.";
    public const string AgeRange = "A idade deve ser de 0 a 130 anos.";

    /// <summary>Texto do backlog (US-06.1, cenário 3).</summary>
    public const string AgeRangeInverted = "A idade mínima não pode ser maior que a idade máxima";

    public const string LocationRequired = "Informe ao menos uma UF ou um município.";
    public const string LocationTooMany = "Informe no máximo 100 UFs e 100 municípios.";
    public const string StateInvalid = "UF inválida. Use a sigla do estado, como SP.";
    public const string CityCodeInvalid = "O município deve ser o código IBGE com 7 dígitos.";

    public const string HistoryWindowRequired = "Informe o período, em dias, a ser considerado no histórico de compras.";
    public const string HistoryWindowRange = "O período do histórico deve ser de 1 a 365 dias.";
    public const string MinAmountRequired = "Informe o valor mínimo gasto.";
    public const string MinAmountPositive = "O valor mínimo gasto deve ser maior que zero, com no máximo 2 casas decimais.";
    public const string MinPurchasesRequired = "Informe a quantidade mínima de compras.";
    public const string MinPurchasesPositive = "A frequência de compras deve ser maior que zero.";
    public const string CategoryRequired = "Informe a categoria adquirida.";
    public const string CategoryNotFound = "A categoria informada não existe.";

    public const string DocumentsRequired = "Informe os CPFs dos clientes.";
    public const string DocumentsTooMany = "A lista pode ter no máximo 10.000 CPFs.";
    public const string DocumentsInvalid = "CPFs inválidos nas posições: {0}.";

    public const string BirthDateInFuture = "A data de nascimento não pode estar no futuro.";
    public const string CustomerStateInvalid = "UF do cliente inválida. Use a sigla do estado, como SP.";
    public const string CustomerCityCodeInvalid = "O município do cliente deve ser o código IBGE com 7 dígitos.";
    public const string HistoryTooMany = "Informe no máximo 10 janelas de histórico.";
    public const string HistoryDuplicated = "Cada janela de histórico deve aparecer uma vez só.";
    public const string HistoryTotalSpentInvalid = "O valor gasto não pode ser negativo e deve ter no máximo 2 casas decimais.";
    public const string HistoryPurchaseCountInvalid = "A quantidade de compras não pode ser negativa.";
    public const string HistoryProductsTooMany = "Informe no máximo 500 produtos por janela de histórico.";
    public const string HistoryProductCodeInvalid = "Informe o código do produto com no máximo 50 caracteres.";

    public const string PageInvalid = "A página deve ser maior que zero.";
    public const string PageSizeInvalid = "O tamanho da página deve ser de 1 a 500.";
}

/// <summary>Critério do público restrito na configuração da campanha; só os parâmetros do tipo (spec GH-047, 2.1).</summary>
/// <param name="Documents">CPFs do <c>SpecificCustomers</c>, com ou sem máscara.</param>
public record EligibilityCriterionRequest(
    string? Type,
    int? MinAge,
    int? MaxAge,
    IReadOnlyList<string?>? States,
    IReadOnlyList<string?>? CityCodes,
    decimal? MinAmount,
    int? MinPurchases,
    Guid? CategoryId,
    int? WindowDays,
    IReadOnlyList<string?>? Documents);

/// <summary>Público restrito na resposta da campanha (D-10). A lista de CPFs sai só como quantidade.</summary>
public record CampaignEligibilityResponse(IReadOnlyList<EligibilityCriterionResponse> Criteria);

public record EligibilityCriterionResponse(
    Guid Id,
    string Type,
    int? MinAge,
    int? MaxAge,
    IReadOnlyList<string>? States,
    IReadOnlyList<string>? CityCodes,
    decimal? MinAmount,
    int? MinPurchases,
    Guid? CategoryId,
    int? WindowDays,
    int? CustomerCount);

/// <summary>Página da lista de clientes específicos (D-10), em ordem de CPF.</summary>
public record EligibleCustomersResponse(int Page, int PageSize, int Total, IReadOnlyList<string> Documents);

/// <summary>Histórico já somado pelo satélite em uma janela (D-4).</summary>
/// <param name="ProductCodes">Produtos comprados na janela; a categoria vem do catálogo.</param>
public record CustomerHistoryRequest(
    [Required(ErrorMessage = EligibilityContract.HistoryWindowRequired)]
    [Range(1, EligibilityContract.MaxWindowDays, ErrorMessage = EligibilityContract.HistoryWindowRange)]
    int? WindowDays,
    decimal? TotalSpent,
    [Range(0, int.MaxValue, ErrorMessage = EligibilityContract.HistoryPurchaseCountInvalid)] int? PurchaseCount,
    [MaxLength(EligibilityContract.MaxHistoryProducts, ErrorMessage = EligibilityContract.HistoryProductsTooMany)]
    IReadOnlyList<string?>? ProductCodes);

/// <summary>Simulação da elegibilidade de um cliente informado (US-06.6, D-17), com os mesmos atributos da concessão.</summary>
public record EligibilitySimulationRequest(
    [Required(ErrorMessage = ConcessionContract.CustomerRequired)] ConcessionCustomerRequest? Customer);

public record EligibilitySimulationResponse(
    Guid CampaignId,
    string? AudienceType,
    bool Eligible,
    IReadOnlyList<CriterionResultResponse> Criteria);

/// <param name="Outcome"><c>Met</c>, <c>NotMet</c> ou <c>Missing</c> (atributo não informado, D-5).</param>
public record CriterionResultResponse(string Type, string Outcome, object Parameters, object? Observed);
