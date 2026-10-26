using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Domain.Campaigns;

public class Campaign
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid NetworkId { get; set; }

    public Network? Network { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Início da vigência (UTC).</summary>
    public DateTime StartsAt { get; set; }

    /// <summary>Fim da vigência (UTC).</summary>
    public DateTime EndsAt { get; set; }

    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;

    /// <summary>Quantidade total de vouchers da campanha (RF06).</summary>
    public int? TotalVoucherLimit { get; set; }

    /// <summary>Limite de vouchers por cliente (RF06c), independente do total; null = sem limite.</summary>
    public int? PerCustomerVoucherLimit { get; set; }

    public VoucherValidityType? VoucherValidityType { get; set; }

    /// <summary>Validade relativa: dias a partir da emissão.</summary>
    public int? VoucherValidityDays { get; set; }

    /// <summary>Validade por data fixa (UTC).</summary>
    public DateTime? VoucherValidUntil { get; set; }

    /// <summary>Teto máximo da validade (UTC), opcional.</summary>
    public DateTime? VoucherValidityCap { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CampaignItem> Items { get; set; } = [];

    public ICollection<CampaignScope> Scopes { get; set; } = [];

    /// <summary>
    /// Cria a campanha em rascunho (PR-01) aplicando a RN02. Datas em UTC; o "agora" é comparado
    /// na precisão de minuto, para um início igual ao minuto atual ser aceito.
    /// </summary>
    /// <exception cref="BusinessRuleException">Período inválido ou início no passado.</exception>
    public static Campaign CreateDraft(
        Guid networkId,
        string name,
        string? description,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        DateTime utcNow)
    {
        EnsureUtc(startsAtUtc, nameof(startsAtUtc));
        EnsureUtc(endsAtUtc, nameof(endsAtUtc));
        EnsureUtc(utcNow, nameof(utcNow));

        // Início igual ao fim também é recusado: o banco exige ends_at > starts_at e a vigência ficaria vazia.
        if (startsAtUtc >= endsAtUtc)
        {
            throw new BusinessRuleException(CampaignErrors.InvalidPeriodCode, CampaignErrors.InvalidPeriodMessage);
        }

        if (startsAtUtc < TruncateToMinute(utcNow))
        {
            throw new BusinessRuleException(CampaignErrors.StartInPastCode, CampaignErrors.StartInPastMessage);
        }

        return new Campaign
        {
            NetworkId = networkId,
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            StartsAt = startsAtUtc,
            EndsAt = endsAtUtc,
            Status = CampaignStatus.Draft,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    /// <summary>Só campanha ativa e dentro da vigência concede voucher (PR-10); rascunho nunca concede.</summary>
    public bool CanIssueVouchers(DateTime utcNow) =>
        Status == CampaignStatus.Active && StartsAt <= utcNow && utcNow < EndsAt;

    /// <summary>Saldo sem cota por alvo (US-01.3, cenário 2); null enquanto o total não foi definido.</summary>
    public int? UndistributedVouchers =>
        TotalVoucherLimit - Items.Sum(item => item.VoucherLimit ?? 0);

    /// <summary>Configuração só muda em rascunho (PR-01); após o vigor vale a RF10a (US-01.7).</summary>
    /// <exception cref="BusinessRuleException">Campanha fora de rascunho.</exception>
    public void EnsureDraft()
    {
        if (Status != CampaignStatus.Draft)
        {
            throw new BusinessRuleException(CampaignErrors.NotDraftCode, CampaignErrors.NotDraftMessage);
        }
    }

    /// <summary>
    /// Substitui os alvos da campanha (US-01.2), aplicando RN05, alvo repetido, RF06a e RN03a (US-01.3).
    /// Se alguma regra falhar, os itens atuais ficam intactos.
    /// </summary>
    /// <exception cref="BusinessRuleException">Regra de negócio violada.</exception>
    public void ReplaceItems(IReadOnlyList<CampaignItem> items, DateTime utcNow)
    {
        EnsureDraft();

        foreach (var item in items)
        {
            EnsureValidBenefit(item);
        }

        // Sobreposição (All + categoria, categoria + produto dela) é permitida; o mesmo alvo duas vezes, não.
        if (items.DistinctBy(item => (item.TargetType, item.CategoryId, item.ProductId)).Count() != items.Count)
        {
            throw new BusinessRuleException(CampaignErrors.DuplicatedTargetCode, CampaignErrors.DuplicatedTargetMessage);
        }

        // RF06a: a cota por alvo só se aplica com mais de um alvo.
        if (items.Count == 1 && items[0].VoucherLimit is not null)
        {
            throw new BusinessRuleException(CampaignErrors.QuotaSingleTargetCode, CampaignErrors.QuotaSingleTargetMessage);
        }

        EnsureQuotasWithinTotal(items, TotalVoucherLimit);

        Items.Clear();

        foreach (var item in items)
        {
            item.CampaignId = Id;
            Items.Add(item);
        }

        UpdatedAt = utcNow;
    }

    /// <summary>Define a quantidade total e o limite por cliente (US-01.3), mantendo a RN03a com as cotas atuais.</summary>
    /// <exception cref="BusinessRuleException">Campanha fora de rascunho ou cotas acima do novo total.</exception>
    public void SetQuantities(int totalVoucherLimit, int? perCustomerVoucherLimit, DateTime utcNow)
    {
        EnsureDraft();
        EnsureQuotasWithinTotal(Items.ToList(), totalVoucherLimit);

        TotalVoucherLimit = totalVoucherLimit;
        PerCustomerVoucherLimit = perCustomerVoucherLimit;
        UpdatedAt = utcNow;
    }

    private static void EnsureValidBenefit(CampaignItem item)
    {
        // RN05.
        if (item.BenefitValue <= 0)
        {
            throw new BusinessRuleException(CampaignErrors.BenefitNotPositiveCode, CampaignErrors.BenefitNotPositiveMessage);
        }

        if (item.BenefitType == BenefitType.Percentage && item.BenefitValue > 100)
        {
            throw new BusinessRuleException(
                CampaignErrors.BenefitPercentageAbove100Code, CampaignErrors.BenefitPercentageAbove100Message);
        }
    }

    /// <summary>RN03a: sem total definido não há o que comparar; a submissão (US-01.6) reavalia.</summary>
    private static void EnsureQuotasWithinTotal(IReadOnlyList<CampaignItem> items, int? totalVoucherLimit)
    {
        var quotas = items.Sum(item => (long)(item.VoucherLimit ?? 0));

        if (totalVoucherLimit is not null && quotas > totalVoucherLimit)
        {
            throw new BusinessRuleException(
                CampaignErrors.QuotasExceedTotalCode,
                string.Format(CampaignErrors.QuotasExceedTotalMessage, Thousands(quotas), Thousands(totalVoucherLimit.Value)));
        }
    }

    // Separador de milhar com ponto, como no backlog ("5.500"), sem depender da cultura do servidor.
    private static string Thousands(long value) =>
        value.ToString("N0", new System.Globalization.NumberFormatInfo { NumberGroupSeparator = "." });

    private static DateTime TruncateToMinute(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMinute, value.Kind);

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("A data deve estar em UTC.", parameterName);
        }
    }
}
