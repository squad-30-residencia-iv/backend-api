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

    /// <summary>Público elegível (US-01.5); null enquanto não definido, o que bloqueia a submissão (RN01).</summary>
    public CampaignAudienceType? AudienceType { get; set; }

    /// <summary>Forma de cálculo da validade do voucher (US-01.12, RF18a); null enquanto não definida.</summary>
    public VoucherValidityType? VoucherValidityType { get; set; }

    /// <summary>Validade relativa: prazo a partir da emissão, na unidade de <see cref="VoucherValidityUnit"/>.</summary>
    public int? VoucherValidityAmount { get; set; }

    public VoucherValidityUnit? VoucherValidityUnit { get; set; }

    /// <summary>Validade por data fixa (UTC).</summary>
    public DateTime? VoucherValidUntil { get; set; }

    /// <summary>Teto máximo da validade relativa (UTC), opcional.</summary>
    public DateTime? VoucherValidityCap { get; set; }

    /// <summary>Valor mínimo da compra para usar o voucher (US-05.2); null = sem mínimo.</summary>
    public decimal? MinPurchaseAmount { get; set; }

    /// <summary>Valor máximo da compra para usar o voucher (US-05.2); null = sem máximo.</summary>
    public decimal? MaxPurchaseAmount { get; set; }

    /// <summary>Utilizações permitidas por voucher (US-05.4); null enquanto não definida.</summary>
    public int? MaxUsesPerVoucher { get; set; }

    /// <summary>Uso do voucher na mesma compra que o gerou (US-01.4, cenário 4); falso = só em compra posterior.</summary>
    public bool AllowUseInSamePurchase { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CampaignItem> Items { get; set; } = [];

    public ICollection<CampaignScope> Scopes { get; set; } = [];

    public ICollection<CampaignCondition> Conditions { get; set; } = [];

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
        EnsureValidPeriod(startsAtUtc, endsAtUtc, utcNow, checkStart: true, checkEnd: false);

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

    /// <summary>
    /// Edita nome, descrição e vigência em rascunho (US-01.7). O início no passado só é recusado quando o início
    /// muda, e o fim no passado só quando o fim muda: um rascunho antigo pode ter só o nome corrigido.
    /// A validade do voucher já configurada continua coerente com a nova vigência (US-01.12, D-8).
    /// </summary>
    /// <exception cref="BusinessRuleException">Campanha fora de rascunho, período inválido (RN02) ou validade incoerente.</exception>
    public void UpdateDetails(string name, string? description, DateTime startsAtUtc, DateTime endsAtUtc, DateTime utcNow)
    {
        EnsureDraft();
        EnsureValidPeriod(
            startsAtUtc, endsAtUtc, utcNow, checkStart: startsAtUtc != StartsAt, checkEnd: endsAtUtc != EndsAt);

        if (VoucherValidUntil is { } validUntil)
        {
            EnsureValidUntilNotBeforeEnd(validUntil, endsAtUtc);
        }

        if (VoucherValidityCap is { } cap)
        {
            EnsureCapAfterStart(cap, startsAtUtc);
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        StartsAt = startsAtUtc;
        EndsAt = endsAtUtc;
        UpdatedAt = utcNow;
    }

    /// <summary>Define o público elegível (US-01.5); o restrito depende dos critérios do EP-06.</summary>
    /// <exception cref="BusinessRuleException">Campanha fora de rascunho ou público restrito.</exception>
    public void SetAudience(CampaignAudienceType audienceType, DateTime utcNow)
    {
        EnsureDraft();

        if (audienceType == CampaignAudienceType.Restricted)
        {
            throw new BusinessRuleException(
                CampaignErrors.AudienceRestrictedUnavailableCode, CampaignErrors.AudienceRestrictedUnavailableMessage);
        }

        AudienceType = audienceType;
        UpdatedAt = utcNow;
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

    /// <summary>Validade relativa (US-01.12), "N dias/horas" a partir da emissão, com teto opcional.</summary>
    /// <exception cref="BusinessRuleException">Campanha fora de rascunho ou teto até o início da campanha (D-7).</exception>
    public void SetRelativeValidity(int amount, VoucherValidityUnit unit, DateTime? capUtc, DateTime utcNow)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        EnsureDraft();

        if (capUtc is { } cap)
        {
            EnsureUtc(cap, nameof(capUtc));
            EnsureCapAfterStart(cap, StartsAt);
        }

        VoucherValidityType = Campaigns.VoucherValidityType.Relative;
        VoucherValidityAmount = amount;
        VoucherValidityUnit = unit;
        VoucherValidityCap = capUtc;
        VoucherValidUntil = null;
        UpdatedAt = utcNow;
    }

    /// <summary>Validade por data fixa (US-01.12), igual para todos os vouchers da campanha.</summary>
    /// <exception cref="BusinessRuleException">Campanha fora de rascunho ou data antes do fim da campanha (D-6).</exception>
    public void SetFixedValidity(DateTime validUntilUtc, DateTime utcNow)
    {
        EnsureUtc(validUntilUtc, nameof(validUntilUtc));
        EnsureDraft();
        EnsureValidUntilNotBeforeEnd(validUntilUtc, EndsAt);

        VoucherValidityType = Campaigns.VoucherValidityType.FixedDate;
        VoucherValidUntil = validUntilUtc;
        VoucherValidityAmount = null;
        VoucherValidityUnit = null;
        VoucherValidityCap = null;
        UpdatedAt = utcNow;
    }

    /// <summary>Restrições de uso no resgate (US-01.12): faixa de valor da compra e utilizações por voucher.</summary>
    /// <exception cref="BusinessRuleException">Campanha fora de rascunho ou mínimo maior que o máximo.</exception>
    public void SetUsageRestrictions(decimal? minPurchaseAmount, decimal? maxPurchaseAmount, int maxUsesPerVoucher, DateTime utcNow)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxUsesPerVoucher);
        EnsureDraft();

        if (minPurchaseAmount > maxPurchaseAmount)
        {
            throw new BusinessRuleException(CampaignErrors.PurchaseRangeInvalidCode, CampaignErrors.PurchaseRangeInvalidMessage);
        }

        MinPurchaseAmount = minPurchaseAmount;
        MaxPurchaseAmount = maxPurchaseAmount;
        MaxUsesPerVoucher = maxUsesPerVoucher;
        UpdatedAt = utcNow;
    }

    /// <summary>
    /// Substitui as condições de geração (US-01.4), cumulativas, e o uso na mesma compra.
    /// Se alguma regra falhar, as condições atuais ficam intactas.
    /// </summary>
    /// <exception cref="BusinessRuleException">Campanha fora de rascunho, tipo indisponível, parâmetro não positivo ou repetição.</exception>
    public void ReplaceConditions(IReadOnlyList<CampaignCondition> conditions, bool allowUseInSamePurchase, DateTime utcNow)
    {
        EnsureDraft();

        foreach (var condition in conditions)
        {
            EnsureValidCondition(condition);
        }

        // Duas categorias diferentes são aceitas (a compra precisa ter as duas); o mesmo tipo e alvo duas vezes, não.
        if (conditions.DistinctBy(condition => (condition.Type, condition.CategoryId, condition.ProductId)).Count() != conditions.Count)
        {
            throw new BusinessRuleException(CampaignErrors.ConditionDuplicatedCode, CampaignErrors.ConditionDuplicatedMessage);
        }

        Conditions.Clear();

        foreach (var condition in conditions)
        {
            condition.CampaignId = Id;
            Conditions.Add(condition);
        }

        AllowUseInSamePurchase = allowUseInSamePurchase;
        UpdatedAt = utcNow;
    }

    private static void EnsureValidCondition(CampaignCondition condition)
    {
        // D2 e semântica indefinida no backlog (spec US-01.4, D-2).
        if (condition.Type is ConditionType.CustomerPurchaseCount or ConditionType.ProductCombination or ConditionType.PurchasePeriod)
        {
            throw new BusinessRuleException(CampaignErrors.ConditionTypeUnavailableCode, CampaignErrors.ConditionTypeUnavailableMessage);
        }

        if (condition.Type == ConditionType.PurchaseTotal && !(condition.MinAmount > 0))
        {
            throw new BusinessRuleException(
                CampaignErrors.ConditionMinAmountNotPositiveCode, CampaignErrors.ConditionMinAmountNotPositiveMessage);
        }

        if (condition.Type == ConditionType.ItemQuantity && !(condition.MinQuantity > 0))
        {
            throw new BusinessRuleException(
                CampaignErrors.ConditionMinQuantityNotPositiveCode, CampaignErrors.ConditionMinQuantityNotPositiveMessage);
        }
    }

    /// <summary>D-6: vouchers emitidos no fim da vigência não podem nascer vencidos.</summary>
    private static void EnsureValidUntilNotBeforeEnd(DateTime validUntilUtc, DateTime endsAtUtc)
    {
        if (validUntilUtc < endsAtUtc)
        {
            throw new BusinessRuleException(
                CampaignErrors.VoucherValidUntilBeforeEndCode, CampaignErrors.VoucherValidUntilBeforeEndMessage);
        }
    }

    /// <summary>D-7: teto antes do fim é permitido (US-04.3, 60 dias com teto em 31/10), mas não até o início.</summary>
    private static void EnsureCapAfterStart(DateTime capUtc, DateTime startsAtUtc)
    {
        if (capUtc <= startsAtUtc)
        {
            throw new BusinessRuleException(
                CampaignErrors.VoucherValidityCapBeforeStartCode, CampaignErrors.VoucherValidityCapBeforeStartMessage);
        }
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

    /// <summary>RN02; o "agora" é comparado na precisão de minuto.</summary>
    private static void EnsureValidPeriod(
        DateTime startsAtUtc, DateTime endsAtUtc, DateTime utcNow, bool checkStart, bool checkEnd)
    {
        EnsureUtc(startsAtUtc, nameof(startsAtUtc));
        EnsureUtc(endsAtUtc, nameof(endsAtUtc));
        EnsureUtc(utcNow, nameof(utcNow));

        // Início igual ao fim também é recusado: o banco exige ends_at > starts_at e a vigência ficaria vazia.
        if (startsAtUtc >= endsAtUtc)
        {
            throw new BusinessRuleException(CampaignErrors.InvalidPeriodCode, CampaignErrors.InvalidPeriodMessage);
        }

        var now = TruncateToMinute(utcNow);

        if (checkStart && startsAtUtc < now)
        {
            throw new BusinessRuleException(CampaignErrors.StartInPastCode, CampaignErrors.StartInPastMessage);
        }

        if (checkEnd && endsAtUtc < now)
        {
            throw new BusinessRuleException(CampaignErrors.EndInPastCode, CampaignErrors.EndInPastMessage);
        }
    }

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
