using VoucherCampaigns.Application.Audit;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Eligibility;
using VoucherCampaigns.Application.Vouchers;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Eligibility;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Campaigns;

public class CampaignService(
    INetworkRepository networks,
    ICampaignRepository campaigns,
    ICatalogRepository catalog,
    IOrganizationRepository organization,
    ICurrentUser currentUser,
    IAuditTrail audit,
    ICampaignVoucherCanceller voucherCanceller,
    TimeProvider timeProvider) : ICampaignService
{
    public async Task<CampaignResponse> CreateAsync(CreateCampaignRequest request, CancellationToken cancellationToken)
    {
        var networkId = InputValues.RequiredId(request.NetworkId, nameof(request.NetworkId), CampaignContract.NetworkRequired);
        NetworkScope.Ensure(currentUser, networkId);

        var network = await networks.FindByIdAsync(networkId, cancellationToken);

        if (network is null || !network.IsActive)
        {
            throw new BusinessRuleException(CampaignErrors.NetworkUnavailableCode, CampaignErrors.NetworkUnavailableMessage);
        }

        // RN02: as datas digitadas valem no fuso da rede (US-02.6).
        var timeZone = NetworkTime.Resolve(network.TimeZone);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var campaign = Campaign.CreateDraft(
            network.Id,
            request.Name,
            request.Description,
            NetworkTime.ToUtc(request.StartsAt!.Value, timeZone, nameof(request.StartsAt)),
            NetworkTime.ToUtc(request.EndsAt!.Value, timeZone, nameof(request.EndsAt)),
            utcNow);

        AppendAudit(campaign, AuditEventTypes.CampaignCreated, currentUser.UserId, utcNow, new CreatedPayload(
            campaign.Name, NetworkTime.ToLocal(campaign.StartsAt, timeZone), NetworkTime.ToLocal(campaign.EndsAt, timeZone), campaign.NetworkId));
        await campaigns.AddAsync(campaign, cancellationToken);

        return Map(campaign, network, utcNow);
    }

    public async Task<IReadOnlyList<CampaignResponse>> GetAllAsync(Guid? networkId, string? status, CancellationToken cancellationToken)
    {
        var scopedNetworkId = InputValues.RequiredId(networkId, "NetworkId", CampaignContract.NetworkRequired);
        NetworkScope.Ensure(currentUser, scopedNetworkId);

        var statusFilter = ParseStatus(status);

        // Rede inativa continua consultável: é só leitura.
        var network = await networks.FindByIdAsync(scopedNetworkId, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.NetworkNotFound);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var items = await campaigns.GetByNetworkAsync(scopedNetworkId, cancellationToken);

        // O filtro olha o status efetivo (PR-04), o mesmo devolvido em "status".
        return items
            .Where(campaign => statusFilter is null || campaign.EffectiveStatus(utcNow) == statusFilter)
            .Select(campaign => Map(campaign, network, utcNow))
            .ToList();
    }

    public async Task<CampaignResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await campaigns.FindByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(CampaignContract.CampaignNotFound);

        // A rede só é conhecida depois de carregar a campanha (US-02.5, cenário 3).
        NetworkScope.Ensure(currentUser, campaign.NetworkId);

        return Map(campaign, campaign.Network!, timeProvider.GetUtcNow().UtcDateTime);
    }

    public async Task<CampaignResponse> ReplaceItemsAsync(
        Guid id,
        ReplaceCampaignItemsRequest request,
        CancellationToken cancellationToken)
    {
        var campaign = await RequireForUpdateAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow();
        var items = new List<CampaignItem>();

        for (var index = 0; index < request.Items!.Count; index++)
        {
            var item = ParseItem(request.Items[index], $"Items[{index}]");
            await EnsureTargetInCatalogAsync(item, cancellationToken);

            // Ids em sequência para os itens voltarem na ordem informada.
            item.Id = Guid.CreateVersion7(utcNow.AddMilliseconds(index));
            items.Add(item);
        }

        var before = SnapshotItems(campaign);
        campaign.ReplaceItems(items, utcNow.UtcDateTime);
        AuditSection(campaign, "items", before, SnapshotItems(campaign), utcNow.UtcDateTime);
        await SaveGuardedAsync(campaign, cancellationToken);

        return Map(campaign, campaign.Network!, utcNow.UtcDateTime);
    }

    public async Task<CampaignResponse> SubmitAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await RequireExistingAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var statusEvent = campaign.Submit(currentUser.UserId, utcNow);
        RecordStatusChange(campaign, statusEvent, AuditEventTypes.CampaignSubmitted);
        await SaveGuardedAsync(campaign, cancellationToken);
        return Map(campaign, campaign.Network!, utcNow);
    }

    public async Task<CampaignResponse> ApproveAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await RequireExistingAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var statusEvent = campaign.Approve(currentUser.UserId, utcNow);
        RecordStatusChange(campaign, statusEvent, AuditEventTypes.CampaignApproved);
        await SaveGuardedAsync(campaign, cancellationToken);
        return Map(campaign, campaign.Network!, utcNow);
    }

    public async Task<CampaignResponse> RejectAsync(Guid id, RejectCampaignRequest? request, CancellationToken cancellationToken)
    {
        var campaign = await RequireExistingAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var statusEvent = campaign.Reject(currentUser.UserId, request?.Reason, utcNow);
        RecordStatusChange(campaign, statusEvent, AuditEventTypes.CampaignRejected);
        await SaveGuardedAsync(campaign, cancellationToken);
        return Map(campaign, campaign.Network!, utcNow);
    }

    public async Task<CampaignResponse> PauseAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await RequireExistingAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var statusEvent = campaign.Pause(currentUser.UserId, utcNow);
        RecordStatusChange(campaign, statusEvent, AuditEventTypes.CampaignPaused);
        await SaveGuardedAsync(campaign, cancellationToken);
        return Map(campaign, campaign.Network!, utcNow);
    }

    public async Task<CampaignResponse> EndAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await RequireExistingAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var statusEvent = campaign.End(currentUser.UserId, utcNow);
        RecordStatusChange(campaign, statusEvent, AuditEventTypes.CampaignEnded);
        await SaveGuardedAsync(campaign, cancellationToken);
        return Map(campaign, campaign.Network!, utcNow);
    }

    public async Task<CampaignResponse> CancelAsync(Guid id, CancelCampaignRequest request, CancellationToken cancellationToken)
    {
        // O [Required] já recusa ausente e em branco; aqui fica a garantia para quem chama o serviço direto (D-4).
        var reason = request.Reason?.Trim();

        if (string.IsNullOrEmpty(reason))
        {
            throw new RequestValidationException(nameof(request.Reason), CampaignContract.CancellationReasonRequired);
        }

        var campaign = await RequireExistingAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var statusEvent = campaign.Cancel(currentUser.UserId, reason, utcNow);
        campaigns.AddStatusEvent(statusEvent);

        // A trava da campanha é o primeiro comando: bloqueia a linha contra reservas em andamento (D-9) e contra
        // outro cancelamento; os vouchers são invalidados na mesma transação (1.3, passos 2 a 6).
        await SaveGuardedAsync(campaign, cancellationToken, async token =>
        {
            var result = await voucherCanceller.CancelVouchersAsync(campaign.Id, currentUser.UserId, utcNow, token);

            AppendAudit(campaign, AuditEventTypes.CampaignCancelled, currentUser.UserId, utcNow, new CancelledPayload(
                statusEvent.FromStatus.ToString(),
                statusEvent.ToStatus.ToString(),
                reason,
                result.CancelledVouchers,
                result.DiscardedProvisional,
                result.ReversedRedemptions));
        });

        return Map(campaign, campaign.Network!, utcNow);
    }

    public async Task<CampaignResponse> ReplaceScopesAsync(
        Guid id,
        ReplaceCampaignScopesRequest request,
        CancellationToken cancellationToken)
    {
        var entries = request.Scopes!.Select((entry, index) => ParseScope(entry, $"Scopes[{index}]")).ToList();
        var campaign = await RequireForUpdateAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow();
        var scopes = new List<CampaignScope>();

        // Uma linha por local e canal; ids em sequência para os locais voltarem na ordem informada.
        foreach (var (regionId, establishmentId, channels) in entries)
        {
            foreach (var channel in channels)
            {
                scopes.Add(new CampaignScope
                {
                    Id = Guid.CreateVersion7(utcNow.AddMilliseconds(scopes.Count)),
                    RegionId = regionId,
                    EstablishmentId = establishmentId,
                    ChannelType = channel
                });
            }
        }

        // As regras do domínio vêm antes da hierarquia, como nas condições. Se um local for recusado,
        // a substituição em memória é descartada com a requisição.
        var before = SnapshotScopes(campaign);
        campaign.ReplaceScopes(scopes, utcNow.UtcDateTime);
        AuditSection(campaign, "scopes", before, SnapshotScopes(campaign), utcNow.UtcDateTime);

        foreach (var entry in entries)
        {
            await EnsureScopeLocationAsync(campaign.NetworkId, entry, cancellationToken);
        }

        await SaveGuardedAsync(campaign, cancellationToken);

        // Relida para trazer o nome da região e da loja de cada local.
        return await MapStoredAsync(campaign.Id, utcNow.UtcDateTime, cancellationToken);
    }

    public async Task<CampaignResponse> DuplicateAsync(Guid id, DuplicateCampaignRequest? request, CancellationToken cancellationToken)
    {
        var source = await campaigns.FindByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(CampaignContract.CampaignNotFound);

        NetworkScope.Ensure(currentUser, source.NetworkId);

        // A lista de clientes específicos não vem na leitura comum (D-18).
        foreach (var document in await campaigns.GetEligibleDocumentsAsync(source.Id, cancellationToken))
        {
            source.EligibleCustomers.Add(new CampaignEligibleCustomer { CampaignId = source.Id, Document = document });
        }

        var timeZone = NetworkTime.Resolve(source.Network!.TimeZone);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var startsAt = request?.StartsAt is { } start ? NetworkTime.ToUtc(start, timeZone, nameof(request.StartsAt)) : source.StartsAt;
        var endsAt = request?.EndsAt is { } end ? NetworkTime.ToUtc(end, timeZone, nameof(request.EndsAt)) : source.EndsAt;

        var copy = source.Duplicate(
            DuplicateName(request?.Name, source.Name),
            DateTime.SpecifyKind(startsAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(endsAt, DateTimeKind.Utc),
            utcNow);

        AppendAudit(copy, AuditEventTypes.CampaignDuplicated, currentUser.UserId, utcNow, new DuplicatePayload(source.Id));
        await campaigns.AddAsync(copy, cancellationToken);

        return await MapStoredAsync(copy.Id, utcNow, cancellationToken);
    }

    /// <summary>Nome informado ou "Cópia de {nome}", dentro do limite da coluna.</summary>
    private static string DuplicateName(string? requested, string sourceName)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested;
        }

        var name = CampaignContract.DuplicateNamePrefix + sourceName;
        return name.Length <= CampaignContract.MaxNameLength ? name : name[..CampaignContract.MaxNameLength];
    }

    public async Task<CampaignResponse> UpdateQuantitiesAsync(
        Guid id,
        UpdateCampaignQuantitiesRequest request,
        CancellationToken cancellationToken)
    {
        var campaign = await RequireForUpdateAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var before = SnapshotQuantities(campaign);
        campaign.SetQuantities(request.TotalVoucherLimit!.Value, request.PerCustomerVoucherLimit, utcNow);
        AuditSection(campaign, "quantities", before, SnapshotQuantities(campaign), utcNow);
        await SaveGuardedAsync(campaign, cancellationToken);

        return Map(campaign, campaign.Network!, utcNow);
    }

    public async Task<CampaignResponse> UpdateAudienceAsync(
        Guid id,
        UpdateCampaignAudienceRequest request,
        CancellationToken cancellationToken)
    {
        var audienceType = ParseEnum<CampaignAudienceType>(
            request.AudienceType, nameof(request.AudienceType), CampaignContract.AudienceTypeInvalid);
        var now = timeProvider.GetUtcNow();
        var audience = EligibilityParsing.ParseCriteria(audienceType, request.Criteria, now);
        var campaign = await RequireForUpdateAsync(id, cancellationToken);
        var utcNow = now.UtcDateTime;

        // Categoria do histórico: existir basta, ativa ou não (o critério olha compras passadas, US-06.2 cenário 3).
        for (var index = 0; index < audience.Criteria.Count; index++)
        {
            if (audience.Criteria[index].CategoryId is { } categoryId
                && await catalog.GetCategoryByIdAsync(categoryId, cancellationToken) is null)
            {
                throw new RequestValidationException($"Criteria[{index}].CategoryId", EligibilityContract.CategoryNotFound);
            }
        }

        await campaigns.LoadEligibleCustomersAsync(campaign, cancellationToken);

        var before = SnapshotAudience(campaign);
        var changes = CustomerListChange.Between(campaign.EligibleCustomers.Select(customer => customer.Document), audience.Documents);
        campaign.SetAudience(audienceType, audience.Criteria, audience.Documents, utcNow);
        AuditSection(campaign, "audience", before, SnapshotAudience(campaign, changes), utcNow);

        // Substituir a lista por diferença exige que ninguém a tenha mudado no meio (clique duplo no portal).
        await SaveGuardedAsync(campaign, cancellationToken);

        return Map(campaign, campaign.Network!, utcNow);
    }

    public async Task<EligibleCustomersResponse> GetEligibleCustomersAsync(
        Guid id, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var number = page ?? 1;
        var size = pageSize ?? EligibilityContract.DefaultCustomersPageSize;

        if (number < 1)
        {
            throw new RequestValidationException("page", EligibilityContract.PageInvalid);
        }

        if (size is < 1 or > EligibilityContract.MaxCustomersPageSize)
        {
            throw new RequestValidationException("pageSize", EligibilityContract.PageSizeInvalid);
        }

        var campaign = await campaigns.FindByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(CampaignContract.CampaignNotFound);

        NetworkScope.Ensure(currentUser, campaign.NetworkId);

        var (total, documents) = await campaigns.GetEligibleDocumentsPageAsync(id, (number - 1) * size, size, cancellationToken);
        return new EligibleCustomersResponse(number, size, total, documents);
    }

    public async Task<EligibilitySimulationResponse> SimulateEligibilityAsync(
        Guid id, EligibilitySimulationRequest request, CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var field = nameof(request.Customer);
        var document = PurchaseParsing.ParseDocument(request.Customer!.Document, $"{field}.{nameof(ConcessionCustomerRequest.Document)}");
        var parsed = EligibilityParsing.ParseProfile(request.Customer, document, utcNow, field);

        var campaign = await campaigns.FindByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(CampaignContract.CampaignNotFound);

        NetworkScope.Ensure(currentUser, campaign.NetworkId);

        // "Todos os clientes" é elegível sem avaliar critérios (US-04.2 cenário 3).
        if (campaign.AudienceType != CampaignAudienceType.Restricted)
        {
            return new EligibilitySimulationResponse(campaign.Id, campaign.AudienceType?.ToString(), true, []);
        }

        var profile = await EligibilityParsing.ResolveAsync(catalog, parsed, cancellationToken);
        var listed = campaign.EligibilityCriteria.Any(criterion => criterion.Type == EligibilityCriterionType.SpecificCustomers)
            && await campaigns.IsCustomerListedAsync(campaign.Id, document, cancellationToken);
        var today = DateOnly.FromDateTime(NetworkTime.ToLocal(utcNow, NetworkTime.Resolve(campaign.Network!.TimeZone)).DateTime);
        var result = EligibilityEvaluator.Evaluate(campaign.EligibilityCriteria, profile, today, listed);

        return new EligibilitySimulationResponse(
            campaign.Id, campaign.AudienceType.ToString(), result.Eligible, EligibilityParsing.MapResults(result));
    }

    public async Task<CampaignResponse> UpdateAsync(Guid id, UpdateCampaignRequest request, CancellationToken cancellationToken)
    {
        var campaign = await RequireForUpdateAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        // RN02: as datas digitadas valem no fuso da rede (US-02.6).
        var timeZone = NetworkTime.Resolve(campaign.Network!.TimeZone);

        var before = SnapshotDetails(campaign, timeZone);
        campaign.UpdateDetails(
            request.Name,
            request.Description,
            NetworkTime.ToUtc(request.StartsAt!.Value, timeZone, nameof(request.StartsAt)),
            NetworkTime.ToUtc(request.EndsAt!.Value, timeZone, nameof(request.EndsAt)),
            utcNow);
        AuditSection(campaign, "details", before, SnapshotDetails(campaign, timeZone), utcNow);
        await SaveGuardedAsync(campaign, cancellationToken);

        return Map(campaign, campaign.Network, utcNow);
    }

    public async Task<CampaignResponse> UpdateVoucherValidityAsync(
        Guid id,
        UpdateVoucherValidityRequest request,
        CancellationToken cancellationToken)
    {
        var type = ParseEnum<VoucherValidityType>(request.Type, nameof(request.Type), CampaignContract.ValidityTypeInvalid);
        var unit = type == VoucherValidityType.Relative ? ParseRelativeValidity(request) : (VoucherValidityUnit?)null;

        if (type == VoucherValidityType.FixedDate)
        {
            EnsureFixedValidityContract(request);
        }

        var campaign = await RequireForUpdateAsync(id, cancellationToken);
        var timeZone = NetworkTime.Resolve(campaign.Network!.TimeZone);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var before = MapValidity(campaign, timeZone);

        if (type == VoucherValidityType.Relative)
        {
            var cap = request.Cap is { } localCap ? NetworkTime.ToUtc(localCap, timeZone, nameof(request.Cap)) : (DateTime?)null;
            campaign.SetRelativeValidity(request.Amount!.Value, unit!.Value, cap, utcNow);
        }
        else
        {
            campaign.SetFixedValidity(NetworkTime.ToUtc(request.ValidUntil!.Value, timeZone, nameof(request.ValidUntil)), utcNow);
        }

        AuditSection(campaign, "voucherValidity", before, MapValidity(campaign, timeZone), utcNow);
        await SaveGuardedAsync(campaign, cancellationToken);

        return Map(campaign, campaign.Network, utcNow);
    }

    public async Task<CampaignResponse> UpdateUsageRestrictionsAsync(
        Guid id,
        UpdateUsageRestrictionsRequest request,
        CancellationToken cancellationToken)
    {
        EnsurePurchaseAmount(request.MinPurchaseAmount, nameof(request.MinPurchaseAmount));
        EnsurePurchaseAmount(request.MaxPurchaseAmount, nameof(request.MaxPurchaseAmount));

        var campaign = await RequireForUpdateAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var before = SnapshotRestrictions(campaign);
        campaign.SetUsageRestrictions(
            request.MinPurchaseAmount,
            request.MaxPurchaseAmount,
            request.MaxUsesPerVoucher!.Value,
            utcNow);
        AuditSection(campaign, "usageRestrictions", before, SnapshotRestrictions(campaign), utcNow);
        await SaveGuardedAsync(campaign, cancellationToken);

        return Map(campaign, campaign.Network!, utcNow);
    }

    public async Task<CampaignResponse> ReplaceConditionsAsync(
        Guid id,
        ReplaceCampaignConditionsRequest request,
        CancellationToken cancellationToken)
    {
        var conditions = request.Conditions!.Select((condition, index) => ParseCondition(condition, $"Conditions[{index}]")).ToList();
        var campaign = await RequireForUpdateAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow();

        // Ids em sequência para as condições voltarem na ordem informada.
        for (var index = 0; index < conditions.Count; index++)
        {
            conditions[index].Id = Guid.CreateVersion7(utcNow.AddMilliseconds(index));
        }

        // As regras do domínio vêm antes do catálogo (spec US-01.4, seção 3). Se o catálogo recusar,
        // nada é gravado: a substituição em memória é descartada com a requisição.
        var before = SnapshotConditions(campaign);
        campaign.ReplaceConditions(conditions, request.AllowUseInSamePurchase!.Value, utcNow.UtcDateTime);

        foreach (var condition in conditions)
        {
            await EnsureConditionInCatalogAsync(condition, cancellationToken);
        }

        AuditSection(campaign, "conditions", before, SnapshotConditions(campaign), utcNow.UtcDateTime);
        await SaveGuardedAsync(campaign, cancellationToken);

        return Map(campaign, campaign.Network!, utcNow.UtcDateTime);
    }

    /// <summary>Prazo e unidade obrigatórios e dentro do limite (D-5); data fixa não se aplica (D-4).</summary>
    private static VoucherValidityUnit ParseRelativeValidity(UpdateVoucherValidityRequest request)
    {
        if (request.ValidUntil is not null)
        {
            throw new RequestValidationException(nameof(request.ValidUntil), CampaignContract.ValidityRelativeWithDate);
        }

        if (request.Amount is null)
        {
            throw new RequestValidationException(nameof(request.Amount), CampaignContract.ValidityAmountRequired);
        }

        if (string.IsNullOrWhiteSpace(request.Unit))
        {
            throw new RequestValidationException(nameof(request.Unit), CampaignContract.ValidityUnitRequired);
        }

        var unit = ParseEnum<VoucherValidityUnit>(request.Unit, nameof(request.Unit), CampaignContract.ValidityUnitInvalid);
        var max = unit == VoucherValidityUnit.Days ? CampaignContract.MaxValidityDays : CampaignContract.MaxValidityHours;

        if (request.Amount < 1 || request.Amount > max)
        {
            throw new RequestValidationException(nameof(request.Amount), CampaignContract.ValidityAmountRange);
        }

        return unit;
    }

    private static void EnsureFixedValidityContract(UpdateVoucherValidityRequest request)
    {
        var unexpected = request.Amount is not null ? nameof(request.Amount)
            : request.Unit is not null ? nameof(request.Unit)
            : request.Cap is not null ? nameof(request.Cap)
            : null;

        if (unexpected is not null)
        {
            throw new RequestValidationException(unexpected, CampaignContract.ValidityFixedWithRelative);
        }

        if (request.ValidUntil is null)
        {
            throw new RequestValidationException(nameof(request.ValidUntil), CampaignContract.ValidUntilRequired);
        }
    }

    /// <summary>Valor de compra opcional; quando informado, cabe em <c>decimal(10,2)</c> e é positivo.</summary>
    private static void EnsurePurchaseAmount(decimal? value, string field)
    {
        if (value is not { } amount)
        {
            return;
        }

        if (amount <= 0)
        {
            throw new RequestValidationException(field, CampaignContract.PurchaseAmountPositive);
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new RequestValidationException(field, CampaignContract.PurchaseAmountScale);
        }

        if (amount > CampaignContract.MaxBenefitValue)
        {
            throw new RequestValidationException(field, CampaignContract.PurchaseAmountTooLarge);
        }
    }

    /// <summary>
    /// Parâmetro do tipo obrigatório e os dos outros tipos ausentes; zero e negativos seguem para o domínio (422).
    /// Tipos indisponíveis passam sem checar parâmetros: o domínio os recusa com 422.
    /// </summary>
    private static CampaignCondition ParseCondition(CampaignConditionRequest? request, string field)
    {
        if (request is null)
        {
            throw new RequestValidationException(field, CampaignContract.ConditionTypeRequired);
        }

        var type = ParseEnum<ConditionType>(request.Type, $"{field}.Type", CampaignContract.ConditionTypeInvalid);
        var condition = new CampaignCondition { Type = type };

        switch (type)
        {
            case ConditionType.PurchaseTotal:
                EnsureOnly(request, field, nameof(request.MinAmount));
                condition.MinAmount = ParseMinAmount(request.MinAmount, $"{field}.MinAmount");
                break;
            case ConditionType.ItemQuantity:
                EnsureOnly(request, field, nameof(request.MinQuantity));
                condition.MinQuantity = request.MinQuantity
                    ?? throw new RequestValidationException($"{field}.MinQuantity", CampaignContract.ConditionMinQuantityRequired);
                break;
            case ConditionType.CustomerPurchaseCount:
                EnsureOnly(request, field, nameof(request.MinQuantity));
                condition.MinQuantity = request.MinQuantity
                    ?? throw new RequestValidationException($"{field}.MinQuantity", CampaignContract.ConditionMinPurchasesRequired);
                break;
            case ConditionType.Category:
                EnsureOnly(request, field, nameof(request.CategoryId));
                condition.CategoryId = NonEmpty(request.CategoryId)
                    ?? throw new RequestValidationException($"{field}.CategoryId", CampaignContract.ConditionCategoryRequired);
                break;
            case ConditionType.Product:
                EnsureOnly(request, field, nameof(request.ProductId));
                condition.ProductId = NonEmpty(request.ProductId)
                    ?? throw new RequestValidationException($"{field}.ProductId", CampaignContract.ConditionProductRequired);
                break;
        }

        return condition;
    }

    /// <summary>Recusa os parâmetros informados que não são do tipo (D-8).</summary>
    private static void EnsureOnly(CampaignConditionRequest request, string field, string allowed)
    {
        var informed = new (string Name, bool HasValue)[]
        {
            (nameof(request.MinAmount), request.MinAmount is not null),
            (nameof(request.MinQuantity), request.MinQuantity is not null),
            (nameof(request.CategoryId), request.CategoryId is not null),
            (nameof(request.ProductId), request.ProductId is not null)
        };

        foreach (var (name, hasValue) in informed)
        {
            if (hasValue && name != allowed)
            {
                throw new RequestValidationException($"{field}.{name}", CampaignContract.ConditionUnexpectedField);
            }
        }
    }

    private static decimal ParseMinAmount(decimal? value, string field)
    {
        var amount = value ?? throw new RequestValidationException(field, CampaignContract.ConditionMinAmountRequired);

        if (decimal.Round(amount, 2) != amount)
        {
            throw new RequestValidationException(field, CampaignContract.ConditionMinAmountScale);
        }

        if (amount > CampaignContract.MaxBenefitValue)
        {
            throw new RequestValidationException(field, CampaignContract.ConditionMinAmountTooLarge);
        }

        return amount;
    }

    /// <summary>Mesmas mensagens dos alvos (EP-02), com códigos próprios da condição (D-9).</summary>
    private async Task EnsureConditionInCatalogAsync(CampaignCondition condition, CancellationToken cancellationToken)
    {
        if (condition.CategoryId is { } categoryId)
        {
            var category = await catalog.GetCategoryByIdAsync(categoryId, cancellationToken)
                ?? throw new BusinessRuleException(CampaignErrors.ConditionCategoryNotFoundCode, CatalogErrors.ProductCategoryNotFoundMessage);

            if (!category.IsActive)
            {
                throw new BusinessRuleException(CampaignErrors.ConditionCategoryInactiveCode, CatalogErrors.ProductCategoryInactiveMessage);
            }
        }

        if (condition.ProductId is { } productId)
        {
            var product = await catalog.GetProductByIdAsync(productId, cancellationToken)
                ?? throw new BusinessRuleException(CampaignErrors.ConditionProductNotFoundCode, CampaignErrors.ItemProductNotFoundMessage);

            if (!product.IsActive)
            {
                throw new BusinessRuleException(CampaignErrors.ConditionProductInactiveCode, CampaignErrors.ItemProductInactiveMessage);
            }
        }
    }

    /// <summary>
    /// Transição no histórico de status e na trilha de auditoria, gravadas no mesmo <c>SaveChanges</c>
    /// da campanha (spec GH-026-trilha-de-auditoria).
    /// </summary>
    private void RecordStatusChange(Campaign campaign, CampaignStatusEvent statusEvent, string eventType)
    {
        campaigns.AddStatusEvent(statusEvent);

        var payload = new StatusChangePayload(
            statusEvent.FromStatus.ToString(), statusEvent.ToStatus.ToString(), statusEvent.Reason);

        AppendAudit(campaign, eventType, statusEvent.ActorUserId, statusEvent.CreatedAt, payload);
    }

    /// <summary>Registro do usuário do token sobre a campanha, com o payload em JSON (spec GH-026-trilha-de-auditoria).</summary>
    private void AppendAudit<TPayload>(Campaign campaign, string eventType, Guid actorUserId, DateTime occurredAt, TPayload payload)
    {
        audit.Append(new AuditEntry
        {
            // Sequência dentro do mesmo instante: a trilha (GH-050) ordena por instante e depois por id.
            Id = AuditIds.Next(occurredAt),
            EventType = eventType,
            ActorType = AuditActorType.User,
            ActorId = actorUserId,
            NetworkId = campaign.NetworkId,
            CampaignId = campaign.Id,
            EntityType = nameof(Campaign),
            EntityId = campaign.Id,
            OccurredAt = occurredAt,
            Payload = AuditJson.Serialize(payload)
        });
    }

    /// <summary>Edição de uma seção do rascunho (spec GH-050, D-8): grava <c>CampaignUpdated</c> só quando algo mudou.</summary>
    /// <summary>
    /// Grava só se a campanha não mudou desde a leitura (PA-10): duas edições ou transições simultâneas não se somam,
    /// e a segunda recebe 422 <see cref="CampaignErrors.ConcurrentUpdateCode"/>.
    /// </summary>
    private async Task SaveGuardedAsync(
        Campaign campaign, CancellationToken cancellationToken, Func<CancellationToken, Task>? withinTransaction = null)
    {
        if (!await campaigns.SaveIfUnchangedAsync(campaign, cancellationToken, withinTransaction))
        {
            throw new BusinessRuleException(CampaignErrors.ConcurrentUpdateCode, CampaignErrors.ConcurrentUpdateMessage);
        }
    }

    private void AuditSection(Campaign campaign, string section, object? before, object? after, DateTime utcNow)
    {
        if (AuditJson.Serialize(before) == AuditJson.Serialize(after))
        {
            return;
        }

        // Seção ainda não configurada vira objeto vazio: o payload sempre tem "before" e "after".
        AppendAudit(campaign, AuditEventTypes.CampaignUpdated, currentUser.UserId, utcNow, new SectionChangePayload(section, before ?? EmptySection, after ?? EmptySection));
    }

    private static DetailsSnapshot SnapshotDetails(Campaign campaign, TimeZoneInfo timeZone) =>
        new(campaign.Name, campaign.Description, NetworkTime.ToLocal(campaign.StartsAt, timeZone), NetworkTime.ToLocal(campaign.EndsAt, timeZone));

    // Sem os ids: a substituição gera ids novos, e só a configuração interessa à trilha.
    private static List<ItemSnapshot> SnapshotItems(Campaign campaign) =>
        campaign.Items.OrderBy(item => item.Id)
            .Select(item => new ItemSnapshot(item.TargetType.ToString(), item.CategoryId, item.ProductId, item.BenefitType.ToString(), Money(item.BenefitValue), item.VoucherLimit))
            .ToList();

    // A lista entra só como quantidade e, no depois, quantos CPFs entraram e saíram (D-20, PA-11): a trilha
    // detecta a troca sem copiar CPFs nem guardar nada que permita confirmar quem está na lista.
    private static AudienceSnapshot SnapshotAudience(Campaign campaign, CustomerListChange? changes = null) =>
        new(campaign.AudienceType?.ToString(), campaign.EligibilityCriteria.OrderBy(criterion => criterion.Id)
            .Select(criterion => new CriterionSnapshot(
                criterion.Type.ToString(),
                criterion.MinAge,
                criterion.MaxAge,
                criterion.States.Count > 0 ? criterion.States : null,
                criterion.CityCodes.Count > 0 ? criterion.CityCodes : null,
                Money(criterion.MinAmount),
                criterion.MinPurchases,
                criterion.CategoryId,
                criterion.WindowDays,
                criterion.CustomerCount))
            .ToList(),
            changes);

    private static QuantitiesSnapshot SnapshotQuantities(Campaign campaign) =>
        new(campaign.TotalVoucherLimit, campaign.PerCustomerVoucherLimit);

    private static RestrictionsSnapshot SnapshotRestrictions(Campaign campaign) =>
        new(Money(campaign.MinPurchaseAmount), Money(campaign.MaxPurchaseAmount), campaign.MaxUsesPerVoucher);

    private static ConditionsSnapshot SnapshotConditions(Campaign campaign) =>
        new(
            campaign.Conditions.OrderBy(condition => condition.Id)
                .Select(condition => new ConditionSnapshot(condition.Type.ToString(), Money(condition.MinAmount), condition.MinQuantity, condition.CategoryId, condition.ProductId))
                .ToList(),
            campaign.AllowUseInSamePurchase);

    private static List<ScopeSnapshot> SnapshotScopes(Campaign campaign) =>
        campaign.Scopes
            .OrderBy(scope => scope.Id)
            .GroupBy(scope => (scope.RegionId, scope.EstablishmentId))
            .Select(location => new ScopeSnapshot(
                location.Key.RegionId,
                location.Key.EstablishmentId,
                location.Select(scope => scope.ChannelType).Distinct().Order().Select(channel => channel.ToString()).ToList()))
            .ToList();

    private sealed record CancelledPayload(
        string FromStatus, string ToStatus, string Reason, int CancelledVouchers, int DiscardedProvisional, int ReversedRedemptions);

    private sealed record CreatedPayload(string Name, DateTimeOffset StartsAt, DateTimeOffset EndsAt, Guid NetworkId);

    private static readonly object EmptySection = new { };

    private sealed record SectionChangePayload(string Section, object Before, object After);

    private sealed record DetailsSnapshot(string Name, string? Description, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

    private sealed record QuantitiesSnapshot(int? TotalVoucherLimit, int? PerCustomerVoucherLimit);

    private sealed record AudienceSnapshot(string? AudienceType, IReadOnlyList<CriterionSnapshot> Criteria, CustomerListChange? CustomerChanges);

    /// <summary>Quantos CPFs entraram e saíram da lista; nulo quando nada mudou, para a repetição idêntica não gerar evento.</summary>
    private sealed record CustomerListChange(int Added, int Removed)
    {
        public static CustomerListChange? Between(IEnumerable<string> current, IEnumerable<string> wanted)
        {
            var before = current.ToHashSet(StringComparer.Ordinal);
            var after = wanted.ToHashSet(StringComparer.Ordinal);
            var added = after.Count(document => !before.Contains(document));
            var removed = before.Count(document => !after.Contains(document));

            return added == 0 && removed == 0 ? null : new CustomerListChange(added, removed);
        }
    }

    private sealed record CriterionSnapshot(
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

    private sealed record RestrictionsSnapshot(decimal? MinPurchaseAmount, decimal? MaxPurchaseAmount, int? MaxUsesPerVoucher);

    /// <summary>Escala fixa de 2 casas: o valor lido do banco e o recém-informado comparam iguais.</summary>
    private static decimal Money(decimal value) => decimal.Round(value, 2) + 0.00m;

    private static decimal? Money(decimal? value) => value is { } amount ? Money(amount) : null;

    private sealed record ItemSnapshot(string TargetType, Guid? CategoryId, Guid? ProductId, string BenefitType, decimal BenefitValue, int? VoucherLimit);

    private sealed record ConditionSnapshot(string Type, decimal? MinAmount, int? MinQuantity, Guid? CategoryId, Guid? ProductId);

    private sealed record ConditionsSnapshot(IReadOnlyList<ConditionSnapshot> Conditions, bool AllowUseInSamePurchase);

    private sealed record ScopeSnapshot(Guid? RegionId, Guid? EstablishmentId, IReadOnlyList<string> Channels);

    private sealed record StatusChangePayload(string FromStatus, string ToStatus, string? Reason);

    private sealed record DuplicatePayload(Guid SourceCampaignId);

    /// <summary>
    /// Região ou loja (as duas vazias = rede inteira) e canais válidos e sem repetição (US-02.3).
    /// Região e loja inexistentes, inativas ou de outra rede ficam para <see cref="EnsureScopeLocationAsync"/> (422).
    /// </summary>
    private static (Guid? RegionId, Guid? EstablishmentId, IReadOnlyList<ChannelType> Channels) ParseScope(
        CampaignScopeRequest? request, string field)
    {
        if (request is null)
        {
            throw new RequestValidationException(field, CampaignContract.ScopeEntryRequired);
        }

        var regionId = NonEmpty(request.RegionId);
        var establishmentId = NonEmpty(request.EstablishmentId);

        if (regionId is not null && establishmentId is not null)
        {
            throw new RequestValidationException($"{field}.EstablishmentId", CampaignContract.ScopeRegionAndEstablishment);
        }

        if (request.Channels is not { Count: > 0 } channels)
        {
            throw new RequestValidationException($"{field}.Channels", CampaignContract.ScopeChannelsRequired);
        }

        var parsed = channels
            .Select(channel => ParseEnum<ChannelType>(channel, $"{field}.Channels", CampaignContract.ScopeChannelInvalid))
            .ToList();

        if (parsed.Distinct().Count() != parsed.Count)
        {
            throw new RequestValidationException($"{field}.Channels", CampaignContract.ScopeChannelsRepeated);
        }

        return (regionId, establishmentId, parsed);
    }

    /// <summary>Local existente, ativo e na rede da campanha; na loja, só canal que ela opera (PR-11).</summary>
    private async Task EnsureScopeLocationAsync(
        Guid networkId,
        (Guid? RegionId, Guid? EstablishmentId, IReadOnlyList<ChannelType> Channels) entry,
        CancellationToken cancellationToken)
    {
        if (entry.EstablishmentId is { } establishmentId)
        {
            var establishment = await organization.GetEstablishmentByIdAsync(establishmentId, cancellationToken)
                ?? throw new BusinessRuleException(
                    CampaignErrors.ScopeEstablishmentNotFoundCode, CampaignErrors.ScopeEstablishmentNotFoundMessage);

            var storeRegion = await organization.GetRegionByIdAsync(establishment.RegionId, cancellationToken);

            if (storeRegion?.NetworkId != networkId)
            {
                throw new BusinessRuleException(CampaignErrors.ScopeOutsideNetworkCode, CampaignErrors.ScopeOutsideNetworkMessage);
            }

            if (!establishment.IsActive)
            {
                throw new BusinessRuleException(
                    CampaignErrors.ScopeEstablishmentInactiveCode, CampaignErrors.ScopeEstablishmentInactiveMessage);
            }

            var enabled = establishment.EnabledChannels();
            var missing = entry.Channels.Where(channel => !enabled.Contains(channel)).ToList();

            if (missing.Count > 0)
            {
                throw new BusinessRuleException(
                    CampaignErrors.ScopeChannelNotEnabledCode,
                    string.Format(CampaignErrors.ScopeChannelNotEnabledMessage, establishment.Code, ChannelLabel(missing[0])));
            }

            return;
        }

        if (entry.RegionId is { } regionId)
        {
            var region = await organization.GetRegionByIdAsync(regionId, cancellationToken)
                ?? throw new BusinessRuleException(
                    CampaignErrors.ScopeRegionNotFoundCode, OrganizationErrors.EstablishmentRegionNotFoundMessage);

            if (region.NetworkId != networkId)
            {
                throw new BusinessRuleException(CampaignErrors.ScopeOutsideNetworkCode, CampaignErrors.ScopeOutsideNetworkMessage);
            }

            if (!region.IsActive)
            {
                throw new BusinessRuleException(
                    CampaignErrors.ScopeRegionInactiveCode, OrganizationErrors.EstablishmentRegionInactiveMessage);
            }
        }
    }

    private static string ChannelLabel(ChannelType channel) => channel == ChannelType.Physical ? "físico" : "virtual";

    /// <summary>Campanha relida do banco, com a rede e os nomes de região e loja do escopo.</summary>
    private async Task<CampaignResponse> MapStoredAsync(Guid id, DateTime utcNow, CancellationToken cancellationToken)
    {
        var stored = await campaigns.FindByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(CampaignContract.CampaignNotFound);

        return Map(stored, stored.Network!, utcNow);
    }

    /// <summary>Campanha existente (404) e da rede do usuário (403).</summary>
    private async Task<Campaign> RequireExistingAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await campaigns.FindForUpdateAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(CampaignContract.CampaignNotFound);

        NetworkScope.Ensure(currentUser, campaign.NetworkId);
        return campaign;
    }

    /// <summary>Campanha existente (404), da rede do usuário (403) e em rascunho (422), nessa ordem.</summary>
    private async Task<Campaign> RequireForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await RequireExistingAsync(id, cancellationToken);
        campaign.EnsureDraft();

        return campaign;
    }

    /// <summary>Tipos e ids coerentes com o alvo; o banco tem o mesmo CHECK, aqui a mensagem é clara.</summary>
    private static CampaignItem ParseItem(CampaignItemRequest? request, string field)
    {
        if (request is null)
        {
            throw new RequestValidationException(field, CampaignContract.TargetTypeRequired);
        }

        var targetType = ParseEnum<TargetType>(request.TargetType, $"{field}.TargetType", CampaignContract.TargetTypeInvalid);
        var benefitType = ParseEnum<BenefitType>(request.BenefitType, $"{field}.BenefitType", CampaignContract.BenefitTypeInvalid);

        var categoryId = NonEmpty(request.CategoryId);
        var productId = NonEmpty(request.ProductId);

        switch (targetType)
        {
            case TargetType.All when categoryId is not null || productId is not null:
                throw new RequestValidationException($"{field}.TargetType", CampaignContract.TargetAllWithId);
            case TargetType.Category when categoryId is null || productId is not null:
                throw new RequestValidationException($"{field}.CategoryId", CampaignContract.TargetCategoryRequired);
            case TargetType.Product when productId is null || categoryId is not null:
                throw new RequestValidationException($"{field}.ProductId", CampaignContract.TargetProductRequired);
        }

        var benefitValue = request.BenefitValue!.Value;

        if (decimal.Round(benefitValue, 2) != benefitValue)
        {
            throw new RequestValidationException($"{field}.BenefitValue", CampaignContract.BenefitValueScale);
        }

        // Negativos e zero seguem para a RN05 (422); só o que não cabe na coluna é erro de contrato.
        if (benefitValue > CampaignContract.MaxBenefitValue)
        {
            throw new RequestValidationException($"{field}.BenefitValue", CampaignContract.BenefitValueTooLarge);
        }

        return new CampaignItem
        {
            TargetType = targetType,
            CategoryId = categoryId,
            ProductId = productId,
            BenefitType = benefitType,
            BenefitValue = benefitValue,
            VoucherLimit = request.VoucherLimit
        };
    }

    /// <summary>Categoria e produto são globais (sem rede); basta existir e estar ativo.</summary>
    private async Task EnsureTargetInCatalogAsync(CampaignItem item, CancellationToken cancellationToken)
    {
        if (item.CategoryId is { } categoryId)
        {
            var category = await catalog.GetCategoryByIdAsync(categoryId, cancellationToken)
                ?? throw new BusinessRuleException(CampaignErrors.ItemCategoryNotFoundCode, CatalogErrors.ProductCategoryNotFoundMessage);

            if (!category.IsActive)
            {
                throw new BusinessRuleException(CampaignErrors.ItemCategoryInactiveCode, CatalogErrors.ProductCategoryInactiveMessage);
            }
        }

        if (item.ProductId is { } productId)
        {
            var product = await catalog.GetProductByIdAsync(productId, cancellationToken)
                ?? throw new BusinessRuleException(CampaignErrors.ItemProductNotFoundCode, CampaignErrors.ItemProductNotFoundMessage);

            if (!product.IsActive)
            {
                throw new BusinessRuleException(CampaignErrors.ItemProductInactiveCode, CampaignErrors.ItemProductInactiveMessage);
            }
        }
    }

    private static Guid? NonEmpty(Guid? value) => value == Guid.Empty ? null : value;

    /// <summary>Aceita só os nomes do enum, sem diferenciar maiúsculas; números são recusados.</summary>
    private static TEnum ParseEnum<TEnum>(string? value, string field, string message)
        where TEnum : struct, Enum
    {
        var trimmed = value?.Trim() ?? string.Empty;

        if (!Enum.GetNames<TEnum>().Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            throw new RequestValidationException(field, message);
        }

        return Enum.Parse<TEnum>(trimmed, ignoreCase: true);
    }

    private static CampaignStatus? ParseStatus(string? value) =>
        value is null ? null : ParseEnum<CampaignStatus>(value, "Status", CampaignContract.StatusInvalid);

    // Status efetivo no instante da leitura (PR-04). IssuedVouchers = 0 até existir a emissão (spec US-01.9, PA-2).
    private static CampaignResponse Map(Campaign campaign, Network network, DateTime utcNow)
    {
        var timeZone = NetworkTime.Resolve(network.TimeZone);

        return new CampaignResponse(
            campaign.Id,
            campaign.NetworkId,
            campaign.Name,
            campaign.Description,
            NetworkTime.ToLocal(campaign.StartsAt, timeZone),
            NetworkTime.ToLocal(campaign.EndsAt, timeZone),
            network.TimeZone,
            campaign.EffectiveStatus(utcNow).ToString(),
            campaign.TotalVoucherLimit,
            campaign.PerCustomerVoucherLimit,
            campaign.UndistributedVouchers,
            campaign.IssuedVouchers,
            campaign.Items.OrderBy(item => item.Id).Select(MapItem).ToList(),
            // O banco devolve o DateTime sem Kind; sem isso o GET sairia sem "Z", diferente do POST.
            DateTime.SpecifyKind(campaign.CreatedAt, DateTimeKind.Utc),
            campaign.AudienceType?.ToString(),
            MapValidity(campaign, timeZone),
            campaign.MinPurchaseAmount,
            campaign.MaxPurchaseAmount,
            campaign.MaxUsesPerVoucher,
            campaign.Conditions.OrderBy(condition => condition.Id).Select(MapCondition).ToList(),
            campaign.AllowUseInSamePurchase,
            campaign.RejectionReason,
            MapScopes(campaign.Scopes),
            campaign.ReservedVouchers,
            MapEligibility(campaign));
    }

    private static CampaignEligibilityResponse? MapEligibility(Campaign campaign) =>
        campaign.AudienceType == CampaignAudienceType.Restricted
            ? new CampaignEligibilityResponse(campaign.EligibilityCriteria.OrderBy(criterion => criterion.Id)
                .Select(criterion => new EligibilityCriterionResponse(
                    criterion.Id,
                    criterion.Type.ToString(),
                    criterion.MinAge,
                    criterion.MaxAge,
                    criterion.Type == EligibilityCriterionType.Location ? criterion.States : null,
                    criterion.Type == EligibilityCriterionType.Location ? criterion.CityCodes : null,
                    criterion.MinAmount,
                    criterion.MinPurchases,
                    criterion.CategoryId,
                    criterion.WindowDays,
                    criterion.CustomerCount))
                .ToList())
            : null;

    /// <summary>Junta as linhas de cada local (uma por canal), na ordem em que o local foi informado.</summary>
    private static List<CampaignScopeResponse> MapScopes(IEnumerable<CampaignScope> scopes) =>
        scopes
            .OrderBy(scope => scope.Id)
            .GroupBy(scope => (scope.RegionId, scope.EstablishmentId))
            .Select(location =>
            {
                var first = location.First();
                return new CampaignScopeResponse(
                    first.RegionId,
                    first.EstablishmentId,
                    first.Establishment?.Name ?? first.Region?.Name,
                    first.Establishment?.Code,
                    location.Select(scope => scope.ChannelType).Distinct().Order().Select(channel => channel.ToString()).ToList());
            })
            .ToList();

    private static VoucherValidityResponse? MapValidity(Campaign campaign, TimeZoneInfo timeZone) =>
        campaign.VoucherValidityType is { } type
            ? new VoucherValidityResponse(
                type.ToString(),
                campaign.VoucherValidityAmount,
                campaign.VoucherValidityUnit?.ToString(),
                campaign.VoucherValidUntil is { } validUntil ? NetworkTime.ToLocal(validUntil, timeZone) : null,
                campaign.VoucherValidityCap is { } cap ? NetworkTime.ToLocal(cap, timeZone) : null)
            : null;

    private static CampaignConditionResponse MapCondition(CampaignCondition condition) =>
        new(
            condition.Id,
            condition.Type.ToString(),
            condition.MinAmount,
            condition.MinQuantity,
            condition.CategoryId,
            condition.ProductId);

    private static CampaignItemResponse MapItem(CampaignItem item) =>
        new(
            item.Id,
            item.TargetType.ToString(),
            item.CategoryId,
            item.ProductId,
            item.BenefitType.ToString(),
            item.BenefitValue,
            item.VoucherLimit);
}
