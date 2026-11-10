using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using VoucherCampaigns.Application.Audit;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Campaigns;

public class CampaignService(
    INetworkRepository networks,
    ICampaignRepository campaigns,
    ICatalogRepository catalog,
    IOrganizationRepository organization,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider timeProvider) : ICampaignService
{
    // camelCase, sem nulos e com acentos legíveis no banco.
    private static readonly JsonSerializerOptions AuditPayloadJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

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

        campaign.ReplaceItems(items, utcNow.UtcDateTime);
        await campaigns.SaveChangesAsync(cancellationToken);

        return Map(campaign, campaign.Network!, utcNow.UtcDateTime);
    }

    public async Task<CampaignResponse> SubmitAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await RequireExistingAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var statusEvent = campaign.Submit(currentUser.UserId, utcNow);
        RecordStatusChange(campaign, statusEvent, AuditEventTypes.CampaignSubmitted);
        await campaigns.SaveChangesAsync(cancellationToken);
        return Map(campaign, campaign.Network!, utcNow);
    }

    public async Task<CampaignResponse> ApproveAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await RequireExistingAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var statusEvent = campaign.Approve(currentUser.UserId, utcNow);
        RecordStatusChange(campaign, statusEvent, AuditEventTypes.CampaignApproved);
        await campaigns.SaveChangesAsync(cancellationToken);
        return Map(campaign, campaign.Network!, utcNow);
    }

    public async Task<CampaignResponse> RejectAsync(Guid id, RejectCampaignRequest? request, CancellationToken cancellationToken)
    {
        var campaign = await RequireExistingAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var statusEvent = campaign.Reject(currentUser.UserId, request?.Reason, utcNow);
        RecordStatusChange(campaign, statusEvent, AuditEventTypes.CampaignRejected);
        await campaigns.SaveChangesAsync(cancellationToken);
        return Map(campaign, campaign.Network!, utcNow);
    }

    public async Task<CampaignResponse> PauseAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await RequireExistingAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var statusEvent = campaign.Pause(currentUser.UserId, utcNow);
        RecordStatusChange(campaign, statusEvent, AuditEventTypes.CampaignPaused);
        await campaigns.SaveChangesAsync(cancellationToken);
        return Map(campaign, campaign.Network!, utcNow);
    }

    public async Task<CampaignResponse> EndAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await RequireExistingAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var statusEvent = campaign.End(currentUser.UserId, utcNow);
        RecordStatusChange(campaign, statusEvent, AuditEventTypes.CampaignEnded);
        await campaigns.SaveChangesAsync(cancellationToken);
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
        campaign.ReplaceScopes(scopes, utcNow.UtcDateTime);

        foreach (var entry in entries)
        {
            await EnsureScopeLocationAsync(campaign.NetworkId, entry, cancellationToken);
        }

        await campaigns.SaveChangesAsync(cancellationToken);

        // Relida para trazer o nome da região e da loja de cada local.
        return await MapStoredAsync(campaign.Id, utcNow.UtcDateTime, cancellationToken);
    }

    public async Task<CampaignResponse> DuplicateAsync(Guid id, DuplicateCampaignRequest? request, CancellationToken cancellationToken)
    {
        var source = await campaigns.FindByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(CampaignContract.CampaignNotFound);

        NetworkScope.Ensure(currentUser, source.NetworkId);

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

        campaign.SetQuantities(request.TotalVoucherLimit!.Value, request.PerCustomerVoucherLimit, utcNow);
        await campaigns.SaveChangesAsync(cancellationToken);

        return Map(campaign, campaign.Network!, utcNow);
    }

    public async Task<CampaignResponse> UpdateAudienceAsync(
        Guid id,
        UpdateCampaignAudienceRequest request,
        CancellationToken cancellationToken)
    {
        var audienceType = ParseEnum<CampaignAudienceType>(
            request.AudienceType, nameof(request.AudienceType), CampaignContract.AudienceTypeInvalid);
        var campaign = await RequireForUpdateAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        campaign.SetAudience(audienceType, utcNow);
        await campaigns.SaveChangesAsync(cancellationToken);

        return Map(campaign, campaign.Network!, utcNow);
    }

    public async Task<CampaignResponse> UpdateAsync(Guid id, UpdateCampaignRequest request, CancellationToken cancellationToken)
    {
        var campaign = await RequireForUpdateAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        // RN02: as datas digitadas valem no fuso da rede (US-02.6).
        var timeZone = NetworkTime.Resolve(campaign.Network!.TimeZone);

        campaign.UpdateDetails(
            request.Name,
            request.Description,
            NetworkTime.ToUtc(request.StartsAt!.Value, timeZone, nameof(request.StartsAt)),
            NetworkTime.ToUtc(request.EndsAt!.Value, timeZone, nameof(request.EndsAt)),
            utcNow);
        await campaigns.SaveChangesAsync(cancellationToken);

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

        if (type == VoucherValidityType.Relative)
        {
            var cap = request.Cap is { } localCap ? NetworkTime.ToUtc(localCap, timeZone, nameof(request.Cap)) : (DateTime?)null;
            campaign.SetRelativeValidity(request.Amount!.Value, unit!.Value, cap, utcNow);
        }
        else
        {
            campaign.SetFixedValidity(NetworkTime.ToUtc(request.ValidUntil!.Value, timeZone, nameof(request.ValidUntil)), utcNow);
        }

        await campaigns.SaveChangesAsync(cancellationToken);

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

        campaign.SetUsageRestrictions(
            request.MinPurchaseAmount,
            request.MaxPurchaseAmount,
            request.MaxUsesPerVoucher!.Value,
            utcNow);
        await campaigns.SaveChangesAsync(cancellationToken);

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
        campaign.ReplaceConditions(conditions, request.AllowUseInSamePurchase!.Value, utcNow.UtcDateTime);

        foreach (var condition in conditions)
        {
            await EnsureConditionInCatalogAsync(condition, cancellationToken);
        }

        await campaigns.SaveChangesAsync(cancellationToken);

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
            Id = Guid.CreateVersion7(new DateTimeOffset(occurredAt)),
            EventType = eventType,
            ActorType = AuditActorType.User,
            ActorId = actorUserId,
            NetworkId = campaign.NetworkId,
            CampaignId = campaign.Id,
            EntityType = nameof(Campaign),
            EntityId = campaign.Id,
            OccurredAt = occurredAt,
            Payload = JsonSerializer.Serialize(payload, AuditPayloadJson)
        });
    }

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
            IssuedVouchers: 0,
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
            MapScopes(campaign.Scopes));
    }

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
