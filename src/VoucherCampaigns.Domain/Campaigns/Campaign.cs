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

    /// <summary>Analista que submeteu o rascunho (US-01.6); nulo enquanto estiver em rascunho.</summary>
    public Guid? SubmittedByUserId { get; set; }

    /// <summary>Instante UTC da submissão.</summary>
    public DateTime? SubmittedAt { get; set; }

    /// <summary>Gestor que aprovou ou reprovou (US-01.11).</summary>
    public Guid? DecidedByUserId { get; set; }

    /// <summary>Instante UTC da aprovação ou reprovação.</summary>
    public DateTime? DecidedAt { get; set; }

    /// <summary>Motivo da reprovação; nulo quando não houve reprovação ou o gestor não informou motivo.</summary>
    public string? RejectionReason { get; set; }

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

    /// <summary>Vouchers confirmados (RN09). Mantido pelo motor em comandos condicionais; não altere na mão.</summary>
    public int IssuedVouchers { get; set; }

    /// <summary>Vouchers provisórios vivos, que contam para o esgotamento (US-04.9, cenário 6).</summary>
    public int ReservedVouchers { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CampaignItem> Items { get; set; } = [];

    public ICollection<CampaignScope> Scopes { get; set; } = [];

    public ICollection<CampaignCondition> Conditions { get; set; } = [];

    public ICollection<CampaignStatusEvent> StatusEvents { get; set; } = [];

    /// <summary>Critérios do público restrito (spec GH-047); vazio no público "todos".</summary>
    public ICollection<CampaignEligibilityCriterion> EligibilityCriteria { get; set; } = [];

    /// <summary>
    /// CPFs da lista de clientes específicos (D-8). Não vem nas leituras comuns da campanha (até 10.000 linhas):
    /// carregue antes de <see cref="SetAudience"/> e de <see cref="Duplicate"/>.
    /// </summary>
    public ICollection<CampaignEligibleCustomer> EligibleCustomers { get; set; } = [];

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

    /// <summary>Público "todos os clientes" (US-01.5): sem critérios e sem lista.</summary>
    public void SetAudience(CampaignAudienceType audienceType, DateTime utcNow) => SetAudience(audienceType, [], [], utcNow);

    /// <summary>
    /// Define o público (US-01.5) e, no restrito, substitui os critérios e a lista de clientes (spec GH-047, D-9).
    /// Os critérios chegam validados pelo contrato; aqui ficam só as regras que valem para qualquer origem.
    /// A lista é aplicada por diferença, para não apagar e regravar os CPFs que continuam.
    /// </summary>
    /// <param name="documents">CPFs válidos e sem repetição, quando há o critério <see cref="EligibilityCriterionType.SpecificCustomers"/>.</param>
    /// <exception cref="BusinessRuleException">Campanha fora de rascunho.</exception>
    public void SetAudience(
        CampaignAudienceType audienceType,
        IReadOnlyList<CampaignEligibilityCriterion> criteria,
        IReadOnlyCollection<string> documents,
        DateTime utcNow)
    {
        EnsureDraft();

        if ((audienceType == CampaignAudienceType.Restricted) != (criteria.Count > 0))
        {
            throw new ArgumentException("O público restrito exige critérios e o público 'todos' não aceita nenhum.", nameof(criteria));
        }

        if (criteria.Any(criterion => criterion.Type == EligibilityCriterionType.SpecificCustomers) != (documents.Count > 0))
        {
            throw new ArgumentException("A lista de CPFs acompanha o critério de clientes específicos.", nameof(documents));
        }

        AudienceType = audienceType;
        EligibilityCriteria.Clear();

        foreach (var criterion in criteria)
        {
            criterion.CampaignId = Id;
            EligibilityCriteria.Add(criterion);
        }

        var wanted = documents.ToHashSet(StringComparer.Ordinal);

        foreach (var removed in EligibleCustomers.Where(customer => !wanted.Contains(customer.Document)).ToList())
        {
            EligibleCustomers.Remove(removed);
        }

        var kept = EligibleCustomers.Select(customer => customer.Document).ToHashSet(StringComparer.Ordinal);

        foreach (var document in wanted.Where(document => !kept.Contains(document)))
        {
            EligibleCustomers.Add(new CampaignEligibleCustomer { CampaignId = Id, Document = document });
        }

        UpdatedAt = utcNow;
    }

    /// <summary>
    /// Status efetivo (PR-04): agendada e ativa seguem a vigência pelo relógio, sem job. O minuto do fim conta
    /// inteiro (D9): às 23:59:59 ainda vale, a partir de 00:00 está encerrada. A pausada vira encerrada no fim (US-01.8).
    /// A ativa sem saldo é esgotada (RN09, spec GH-045 D-5): as reservas contam e, se vencem, ela volta a ativa.
    /// Os demais status valem como foram gravados (spec GH-026-status-efetivo).
    /// </summary>
    public CampaignStatus EffectiveStatus(DateTime utcNow)
    {
        if (Status is not (CampaignStatus.Scheduled or CampaignStatus.Active or CampaignStatus.Paused))
        {
            return Status;
        }

        if (BusinessTime.HasPassed(EndsAt, utcNow))
        {
            return CampaignStatus.Ended;
        }

        if (Status == CampaignStatus.Paused)
        {
            return CampaignStatus.Paused;
        }

        if (utcNow < StartsAt)
        {
            return CampaignStatus.Scheduled;
        }

        return HasAvailableVouchers ? CampaignStatus.Active : CampaignStatus.SoldOut;
    }

    /// <summary>Saldo para novas emissões: total menos confirmados e reservados; sem total, nunca esgota.</summary>
    public bool HasAvailableVouchers =>
        TotalVoucherLimit is null || IssuedVouchers + ReservedVouchers < TotalVoucherLimit;

    /// <summary>
    /// A campanha vale na loja e no canal (RN08, US-02.3) quando alguma linha do escopo casa: a rede inteira,
    /// a região da loja ou a própria loja, sempre com o canal da compra.
    /// </summary>
    public bool IsInScope(Establishment establishment, ChannelType channel) =>
        Scopes.Any(scope =>
            scope.ChannelType == channel
            && (scope.EstablishmentId is null && scope.RegionId is null
                || scope.RegionId == establishment.RegionId
                || scope.EstablishmentId == establishment.Id));

    /// <summary>Condições de geração (US-04.1), cumulativas; sem alvo configurado não há o que conceder.</summary>
    public bool IsSatisfiedBy(Vouchers.Purchase purchase) =>
        Items.Count > 0 && Conditions.All(condition => condition.IsSatisfiedBy(purchase));

    /// <summary>
    /// Alvo que origina o voucher (US-04.3, spec GH-045 D-10): o mais específico presente na compra (produto antes
    /// de categoria); sem nenhum, o alvo "todos"; sem "todos", o primeiro configurado.
    /// </summary>
    /// <exception cref="InvalidOperationException">Campanha sem alvo (a submissão exige ao menos um).</exception>
    public CampaignItem ChooseTarget(Vouchers.Purchase purchase)
    {
        var ordered = Items.OrderBy(item => item.Id).ToList();

        if (ordered.Count == 0)
        {
            throw new InvalidOperationException("A campanha não tem alvo configurado.");
        }

        return ordered.FirstOrDefault(item => item.TargetType == TargetType.Product && purchase.Items.Any(item.Matches))
            ?? ordered.FirstOrDefault(item => item.TargetType == TargetType.Category && purchase.Items.Any(item.Matches))
            ?? ordered.FirstOrDefault(item => item.TargetType == TargetType.All)
            ?? ordered[0];
    }

    /// <summary>Só campanha efetivamente ativa concede voucher (PR-10); rascunho nunca concede.</summary>
    public bool CanIssueVouchers(DateTime utcNow) => EffectiveStatus(utcNow) == CampaignStatus.Active;

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

    /// <summary>
    /// Envia o rascunho para aprovação (US-01.6). Exige público, alvo, quantidade total, validade e utilizações.
    /// </summary>
    /// <exception cref="BusinessRuleException">Fora de rascunho ou configuração incompleta.</exception>
    public CampaignStatusEvent Submit(Guid actorUserId, DateTime utcNow)
    {
        EnsureUtc(utcNow, nameof(utcNow));

        if (Status != CampaignStatus.Draft)
        {
            throw new BusinessRuleException(CampaignErrors.SubmitNotDraftCode, CampaignErrors.SubmitNotDraftMessage);
        }

        if (AudienceType is null)
        {
            throw new BusinessRuleException(CampaignErrors.SubmitAudienceRequiredCode, CampaignErrors.SubmitAudienceRequiredMessage);
        }

        if (Items.Count == 0)
        {
            throw new BusinessRuleException(CampaignErrors.SubmitItemsRequiredCode, CampaignErrors.SubmitItemsRequiredMessage);
        }

        if (TotalVoucherLimit is null)
        {
            throw new BusinessRuleException(CampaignErrors.SubmitTotalRequiredCode, CampaignErrors.SubmitTotalRequiredMessage);
        }

        if (VoucherValidityType is null)
        {
            throw new BusinessRuleException(CampaignErrors.SubmitValidityRequiredCode, CampaignErrors.SubmitValidityRequiredMessage);
        }

        if (MaxUsesPerVoucher is null)
        {
            throw new BusinessRuleException(CampaignErrors.SubmitMaxUsesRequiredCode, CampaignErrors.SubmitMaxUsesRequiredMessage);
        }

        if (Scopes.Count == 0)
        {
            throw new BusinessRuleException(CampaignErrors.SubmitScopeRequiredCode, CampaignErrors.SubmitScopeRequiredMessage);
        }

        SubmittedByUserId = actorUserId;
        SubmittedAt = utcNow;
        return MoveTo(CampaignStatus.PendingApproval, actorUserId, null, utcNow);
    }

    /// <summary>
    /// Aprova a campanha em análise (US-01.11). Início futuro fica agendada; vigência já iniciada fica ativa.
    /// </summary>
    /// <exception cref="BusinessRuleException">Fora de aprovação ou vigência já encerrada.</exception>
    public CampaignStatusEvent Approve(Guid actorUserId, DateTime utcNow)
    {
        EnsureUtc(utcNow, nameof(utcNow));
        EnsurePending();

        // D9: o minuto do fim conta inteiro.
        if (BusinessTime.HasPassed(EndsAt, utcNow))
        {
            throw new BusinessRuleException(CampaignErrors.ApprovePeriodEndedCode, CampaignErrors.ApprovePeriodEndedMessage);
        }

        DecidedByUserId = actorUserId;
        DecidedAt = utcNow;
        RejectionReason = null;
        var next = utcNow < StartsAt ? CampaignStatus.Scheduled : CampaignStatus.Active;
        return MoveTo(next, actorUserId, null, utcNow);
    }

    /// <summary>Reprova a campanha em análise (US-01.11). Motivo em branco fica nulo. Não volta a rascunho.</summary>
    /// <exception cref="BusinessRuleException">Fora de aprovação.</exception>
    public CampaignStatusEvent Reject(Guid actorUserId, string? reason, DateTime utcNow)
    {
        EnsureUtc(utcNow, nameof(utcNow));
        EnsurePending();

        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        DecidedByUserId = actorUserId;
        DecidedAt = utcNow;
        RejectionReason = trimmed;
        return MoveTo(CampaignStatus.Rejected, actorUserId, trimmed, utcNow);
    }

    /// <summary>
    /// Substitui onde a campanha vale (US-02.3): uma linha por local (rede, região ou loja) e canal.
    /// A existência, a rede e os canais da loja são conferidos pelo serviço.
    /// </summary>
    /// <exception cref="BusinessRuleException">Campanha fora de rascunho ou local repetido com o mesmo canal.</exception>
    public void ReplaceScopes(IReadOnlyList<CampaignScope> scopes, DateTime utcNow)
    {
        EnsureDraft();

        if (scopes.DistinctBy(scope => (scope.RegionId, scope.EstablishmentId, scope.ChannelType)).Count() != scopes.Count)
        {
            throw new BusinessRuleException(CampaignErrors.ScopeDuplicatedCode, CampaignErrors.ScopeDuplicatedMessage);
        }

        Scopes.Clear();

        foreach (var scope in scopes)
        {
            scope.CampaignId = Id;
            Scopes.Add(scope);
        }

        UpdatedAt = utcNow;
    }

    /// <summary>Pausa a campanha efetivamente ativa (US-01.8). Não há retomada: a pausada só pode ser encerrada.</summary>
    /// <exception cref="BusinessRuleException">Campanha que não está ativa.</exception>
    public CampaignStatusEvent Pause(Guid actorUserId, DateTime utcNow)
    {
        EnsureUtc(utcNow, nameof(utcNow));

        if (EffectiveStatus(utcNow) != CampaignStatus.Active)
        {
            throw new BusinessRuleException(CampaignErrors.PauseNotActiveCode, CampaignErrors.PauseNotActiveMessage);
        }

        // A agendada que já começou é ativa: o histórico registra o status efetivo.
        Status = CampaignStatus.Active;
        return MoveTo(CampaignStatus.Paused, actorUserId, null, utcNow);
    }

    /// <summary>Encerra a campanha ativa ou pausada antes do fim da vigência (US-01.8).</summary>
    /// <exception cref="BusinessRuleException">Campanha que não está ativa nem pausada.</exception>
    public CampaignStatusEvent End(Guid actorUserId, DateTime utcNow)
    {
        EnsureUtc(utcNow, nameof(utcNow));

        var current = EffectiveStatus(utcNow);

        if (current is not (CampaignStatus.Active or CampaignStatus.Paused))
        {
            throw new BusinessRuleException(CampaignErrors.EndNotAllowedCode, CampaignErrors.EndNotAllowedMessage);
        }

        Status = current;
        return MoveTo(CampaignStatus.Ended, actorUserId, null, utcNow);
    }

    /// <summary>
    /// Cancela a campanha agendada, ativa, pausada ou esgotada pelo status efetivo (US-01.8, RN10; spec GH-061, D-2 a D-4).
    /// O histórico registra o status efetivo de origem e o motivo sem espaços nas pontas. Os vouchers ficam com o serviço.
    /// </summary>
    /// <exception cref="BusinessRuleException">Status efetivo fora de agendada, ativa, pausada ou esgotada.</exception>
    public CampaignStatusEvent Cancel(Guid actorUserId, string reason, DateTime utcNow)
    {
        EnsureUtc(utcNow, nameof(utcNow));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var current = EffectiveStatus(utcNow);

        if (current is not (CampaignStatus.Scheduled or CampaignStatus.Active or CampaignStatus.Paused or CampaignStatus.SoldOut))
        {
            throw new BusinessRuleException(CampaignErrors.CancelNotAllowedCode, CampaignErrors.CancelNotAllowedMessage);
        }

        Status = current;
        return MoveTo(CampaignStatus.Cancelled, actorUserId, reason.Trim(), utcNow);
    }

    /// <summary>
    /// Rascunho novo com a configuração desta campanha, de qualquer status (US-01.10), na mesma rede e com a RN02.
    /// Teto e data fixa da validade só vêm se continuarem coerentes com a nova vigência (spec US-01.10, D-2).
    /// </summary>
    /// <exception cref="BusinessRuleException">Período inválido ou início no passado.</exception>
    public Campaign Duplicate(string name, DateTime startsAtUtc, DateTime endsAtUtc, DateTime utcNow)
    {
        var copy = CreateDraft(NetworkId, name, Description, startsAtUtc, endsAtUtc, utcNow);

        copy.TotalVoucherLimit = TotalVoucherLimit;
        copy.PerCustomerVoucherLimit = PerCustomerVoucherLimit;
        copy.AudienceType = AudienceType;
        copy.MinPurchaseAmount = MinPurchaseAmount;
        copy.MaxPurchaseAmount = MaxPurchaseAmount;
        copy.MaxUsesPerVoucher = MaxUsesPerVoucher;
        copy.AllowUseInSamePurchase = AllowUseInSamePurchase;

        if (VoucherValidityType == Campaigns.VoucherValidityType.Relative)
        {
            copy.VoucherValidityType = VoucherValidityType;
            copy.VoucherValidityAmount = VoucherValidityAmount;
            copy.VoucherValidityUnit = VoucherValidityUnit;
            copy.VoucherValidityCap = VoucherValidityCap > copy.StartsAt ? VoucherValidityCap : null;
        }
        else if (VoucherValidityType == Campaigns.VoucherValidityType.FixedDate && VoucherValidUntil >= copy.EndsAt)
        {
            copy.VoucherValidityType = VoucherValidityType;
            copy.VoucherValidUntil = VoucherValidUntil;
        }

        // Ids em sequência para itens, condições e locais manterem a ordem da origem.
        var sequence = 0;
        Guid NextId() => Guid.CreateVersion7(new DateTimeOffset(utcNow).AddMilliseconds(sequence++));

        foreach (var item in Items.OrderBy(item => item.Id))
        {
            copy.Items.Add(new CampaignItem
            {
                Id = NextId(),
                CampaignId = copy.Id,
                TargetType = item.TargetType,
                CategoryId = item.CategoryId,
                ProductId = item.ProductId,
                BenefitType = item.BenefitType,
                BenefitValue = item.BenefitValue,
                VoucherLimit = item.VoucherLimit
            });
        }

        foreach (var condition in Conditions.OrderBy(condition => condition.Id))
        {
            copy.Conditions.Add(new CampaignCondition
            {
                Id = NextId(),
                CampaignId = copy.Id,
                Type = condition.Type,
                MinAmount = condition.MinAmount,
                MinQuantity = condition.MinQuantity,
                CategoryId = condition.CategoryId,
                ProductId = condition.ProductId
            });
        }

        // Público restrito com critérios e lista (spec GH-047, D-18); a lista precisa ter sido carregada.
        foreach (var criterion in EligibilityCriteria.OrderBy(criterion => criterion.Id))
        {
            copy.EligibilityCriteria.Add(new CampaignEligibilityCriterion
            {
                Id = NextId(),
                CampaignId = copy.Id,
                Type = criterion.Type,
                MinAge = criterion.MinAge,
                MaxAge = criterion.MaxAge,
                States = [.. criterion.States],
                CityCodes = [.. criterion.CityCodes],
                MinAmount = criterion.MinAmount,
                MinPurchases = criterion.MinPurchases,
                CategoryId = criterion.CategoryId,
                WindowDays = criterion.WindowDays,
                CustomerCount = criterion.CustomerCount
            });
        }

        foreach (var customer in EligibleCustomers)
        {
            copy.EligibleCustomers.Add(new CampaignEligibleCustomer { CampaignId = copy.Id, Document = customer.Document });
        }

        foreach (var scope in Scopes.OrderBy(scope => scope.Id))
        {
            copy.Scopes.Add(new CampaignScope
            {
                Id = NextId(),
                CampaignId = copy.Id,
                RegionId = scope.RegionId,
                EstablishmentId = scope.EstablishmentId,
                ChannelType = scope.ChannelType
            });
        }

        return copy;
    }

    private void EnsurePending()
    {
        if (Status != CampaignStatus.PendingApproval)
        {
            throw new BusinessRuleException(CampaignErrors.DecisionNotPendingCode, CampaignErrors.DecisionNotPendingMessage);
        }
    }

    private CampaignStatusEvent MoveTo(CampaignStatus next, Guid actorUserId, string? reason, DateTime utcNow)
    {
        var from = Status;
        Status = next;
        UpdatedAt = utcNow;

        return new CampaignStatusEvent
        {
            Id = Guid.CreateVersion7(new DateTimeOffset(utcNow)),
            CampaignId = Id,
            FromStatus = from,
            ToStatus = next,
            ActorUserId = actorUserId,
            Reason = reason,
            CreatedAt = utcNow,
        };
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
        // Semântica indefinida no backlog (spec US-01.4, D-2). A quantidade de compras foi liberada pela decisão D2 (GH-045, D-11).
        if (condition.Type is ConditionType.ProductCombination or ConditionType.PurchasePeriod)
        {
            throw new BusinessRuleException(CampaignErrors.ConditionTypeUnavailableCode, CampaignErrors.ConditionTypeUnavailableMessage);
        }

        if (condition.Type == ConditionType.CustomerPurchaseCount && !(condition.MinQuantity > 0))
        {
            throw new BusinessRuleException(
                CampaignErrors.ConditionMinPurchasesNotPositiveCode, CampaignErrors.ConditionMinPurchasesNotPositiveMessage);
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
