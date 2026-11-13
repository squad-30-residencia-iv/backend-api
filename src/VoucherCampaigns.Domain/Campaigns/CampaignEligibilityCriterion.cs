namespace VoucherCampaigns.Domain.Campaigns;

/// <summary>
/// Critério do público restrito (spec GH-047, D-3 e D-19). Cada tipo usa só os seus parâmetros; o banco tem o mesmo
/// <c>CHECK</c>. Os critérios da campanha valem juntos (AND, D-6).
/// </summary>
public class CampaignEligibilityCriterion
{
    public Guid Id { get; set; }

    public Guid CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    public EligibilityCriterionType Type { get; set; }

    public int? MinAge { get; set; }

    public int? MaxAge { get; set; }

    /// <summary>Siglas de UF em maiúsculas; o cliente atende se a UF dele estiver aqui ou o município em <see cref="CityCodes"/>.</summary>
    public List<string> States { get; set; } = [];

    /// <summary>Códigos IBGE de município, 7 dígitos.</summary>
    public List<string> CityCodes { get; set; } = [];

    public decimal? MinAmount { get; set; }

    public int? MinPurchases { get; set; }

    public Guid? CategoryId { get; set; }

    /// <summary>Janela do histórico em dias, de 1 a 365 (RN12, D-4).</summary>
    public int? WindowDays { get; set; }

    /// <summary>Quantidade de CPFs da lista (<see cref="EligibilityCriterionType.SpecificCustomers"/>), para a leitura e a auditoria.</summary>
    public int? CustomerCount { get; set; }

    /// <summary>SHA-256 da lista ordenada (D-20): a trilha detecta a troca sem guardar CPFs.</summary>
    public string? CustomersDigest { get; set; }
}

/// <summary>CPF da lista de clientes específicos da campanha (D-8, D-19).</summary>
public class CampaignEligibleCustomer
{
    public Guid CampaignId { get; set; }

    /// <summary>Só os 11 dígitos, com dígito verificador válido.</summary>
    public string Document { get; set; } = string.Empty;
}
