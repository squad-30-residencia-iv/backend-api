using System.Globalization;
using VoucherCampaigns.Application.Audit;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.Customers;
using VoucherCampaigns.Application.Dashboard;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Eligibility;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Application.Vouchers;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Eligibility;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Application.Campaigns;

public class CampaignService(
    INetworkRepository networks,
    ICampaignRepository campaigns,
    ICatalogRepository catalog,
    IOrganizationRepository organization,
    IDashboardRepository voucherCounts,
    ICurrentUser currentUser,
    IAuditTrail audit,
    ICampaignVoucherCanceller voucherCanceller,
    ICustomerGroupService customerGroups,
    ICustomerGroupRepository customerGroupStore,
    ICampaignTagRepository campaignTags,
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

    public async Task<IReadOnlyList<CampaignResponse>> GetAllAsync(Guid? networkId, string? status, CancellationToken cancellationToken) =>
        (await ListAsync(networkId, status, null, null, cancellationToken)).Items;

    /// <summary>Listagem com o filtro de status e de tag no banco e a página opcional (specs GH-071 D-5 e GH-077 D-4).</summary>
    public async Task<PagedResult<CampaignResponse>> ListAsync(
        Guid? networkId, string? status, int? page, int? pageSize, CancellationToken cancellationToken, Guid? tagId = null)
    {
        var scopedNetworkId = InputValues.RequiredId(networkId, "NetworkId", CampaignContract.NetworkRequired);
        NetworkScope.Ensure(currentUser, scopedNetworkId);

        var statusFilter = ParseStatus(status);
        var paging = PageRequest.Parse(page, pageSize);

        // Rede inativa continua consultável: é só leitura.
        var network = await networks.FindByIdAsync(scopedNetworkId, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.NetworkNotFound);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var (total, items) = await campaigns.GetPageByNetworkAsync(
            scopedNetworkId, statusFilter, utcNow, paging?.Skip, paging?.Take, cancellationToken, tagId);
        var counters = await LoadCountersAsync(scopedNetworkId, null, utcNow, cancellationToken);
        var uniques = await customerGroupStore.CountDistinctByCampaignAsync(items.Select(campaign => campaign.Id).ToList(), cancellationToken);

        // O filtro olha o status efetivo (PR-04), o mesmo devolvido em "status"; o banco já filtrou pela mesma regra.
        var responses = items
            .Where(campaign => statusFilter is null || campaign.EffectiveStatus(utcNow) == statusFilter)
            .Select(campaign => Map(campaign, network, utcNow, counters.GetValueOrDefault(campaign.Id), uniques.GetValueOrDefault(campaign.Id)))
            .ToList();

        return new PagedResult<CampaignResponse>(responses, total);
    }

    public async Task<CampaignResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await campaigns.FindByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(CampaignContract.CampaignNotFound);

        // A rede só é conhecida depois de carregar a campanha (US-02.5, cenário 3).
        NetworkScope.Ensure(currentUser, campaign.NetworkId);

        return await RespondAsync(campaign, campaign.Network!, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
    }

    public async Task<CampaignResponse> ReplaceItemsAsync(
        Guid id,
        ReplaceCampaignItemsRequest request,
        CancellationToken cancellationToken)
    {
        var campaign = await RequireForUpdateAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow();
        var parsed = new List<(CampaignItem Item, string? ProductCode)>();

        for (var index = 0; index < request.Items!.Count; index++)
        {
            var (item, productCode) = ParseItem(request.Items[index], $"Items[{index}]");

            // Ids em sequência para os itens voltarem na ordem informada.
            item.Id = Guid.CreateVersion7(utcNow.AddMilliseconds(index));
            parsed.Add((item, productCode));
        }

        await ResolveProductCodesAsync(parsed, cancellationToken);
        var items = parsed.Select(entry => entry.Item).ToList();
        await EnsureTargetsInCatalogAsync(items, cancellationToken);

        var before = SnapshotItems(campaign);
        campaign.ReplaceItems(items, utcNow.UtcDateTime);
        AuditItems(campaign, before, SnapshotItems(campaign), utcNow.UtcDateTime);
        await SaveGuardedAsync(campaign, cancellationToken);

        // Relida para a resposta trazer o código de cada produto (spec GH-068, D-2).
        return await MapStoredAsync(campaign.Id, utcNow.UtcDateTime, cancellationToken);
    }

    public async Task<CampaignResponse> SubmitAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await RequireExistingAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var unique = await customerGroupStore.CountDistinctDocumentsAsync(
            campaign.CustomerGroups.Select(link => link.GroupId).ToList(), cancellationToken);
        var statusEvent = campaign.Submit(currentUser.UserId, utcNow, unique);
        RecordStatusChange(campaign, statusEvent, AuditEventTypes.CampaignSubmitted);
        await SaveGuardedAsync(campaign, cancellationToken);
        return await RespondAsync(campaign, campaign.Network!, utcNow, cancellationToken);
    }

    public async Task<CampaignResponse> ApproveAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await RequireExistingAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var statusEvent = campaign.Approve(currentUser.UserId, utcNow);
        RecordStatusChange(campaign, statusEvent, AuditEventTypes.CampaignApproved);
        await SaveGuardedAsync(campaign, cancellationToken);
        return await RespondAsync(campaign, campaign.Network!, utcNow, cancellationToken);
    }

    public async Task<CampaignResponse> RejectAsync(Guid id, RejectCampaignRequest? request, CancellationToken cancellationToken)
    {
        var campaign = await RequireExistingAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var statusEvent = campaign.Reject(currentUser.UserId, request?.Reason, utcNow);
        RecordStatusChange(campaign, statusEvent, AuditEventTypes.CampaignRejected);
        await SaveGuardedAsync(campaign, cancellationToken);
        return await RespondAsync(campaign, campaign.Network!, utcNow, cancellationToken);
    }

    public async Task<CampaignResponse> PauseAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await RequireExistingAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var statusEvent = campaign.Pause(currentUser.UserId, utcNow);
        RecordStatusChange(campaign, statusEvent, AuditEventTypes.CampaignPaused);
        await SaveGuardedAsync(campaign, cancellationToken);
        return await RespondAsync(campaign, campaign.Network!, utcNow, cancellationToken);
    }

    public async Task<CampaignResponse> EndAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await RequireExistingAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var statusEvent = campaign.End(currentUser.UserId, utcNow);
        RecordStatusChange(campaign, statusEvent, AuditEventTypes.CampaignEnded);
        await SaveGuardedAsync(campaign, cancellationToken);
        return await RespondAsync(campaign, campaign.Network!, utcNow, cancellationToken);
    }

    public async Task<CampaignResponse> CancelAsync(Guid id, CancelCampaignRequest request, CancellationToken cancellationToken)
    {
        // O [Required] já recusa ausente e em branco; aqui fica a garantia para quem chama o serviço direto (D-4).
        var reason = request.Reason?.Trim();

        if (string.IsNullOrEmpty(reason))
        {
            throw new RequestValidationException(nameof(request.Reason), CampaignContract.CancellationReasonRequired);
        }

        if (reason.Length > CampaignContract.MaxCancellationReasonLength)
        {
            throw new RequestValidationException(nameof(request.Reason), CampaignContract.CancellationReasonTooLong);
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

        return await RespondAsync(campaign, campaign.Network!, utcNow, cancellationToken);
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
        AuditScopes(campaign, before, SnapshotScopes(campaign), utcNow.UtcDateTime);

        await EnsureScopeLocationsAsync(campaign.NetworkId, entries, cancellationToken);

        await SaveGuardedAsync(campaign, cancellationToken);

        // Relida para trazer o nome da região e da loja de cada local.
        return await MapStoredAsync(campaign.Id, utcNow.UtcDateTime, cancellationToken);
    }

    public async Task<CampaignResponse> ReplaceCustomerGroupsAsync(
        Guid id, ReplaceCampaignCustomerGroupsRequest request, CancellationToken cancellationToken)
    {
        var campaign = await RequireForUpdateAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var groupIds = (request.GroupIds ?? []).Where(groupId => groupId is not null).Select(groupId => groupId!.Value).Distinct().ToList();
        var unique = groupIds.Count == 0
            ? 0
            : await customerGroups.CountActiveAsync(campaign.NetworkId, groupIds, cancellationToken);

        var before = campaign.CustomerGroups.Select(link => link.GroupId).Order().ToList();
        campaign.ReplaceCustomerGroups(groupIds, utcNow);
        if (groupIds.Count > 0 && campaign.PerCustomerVoucherLimit is not null && campaign.TotalVoucherLimit is not null)
        {
            campaign.EnsureCustomerGroupQuota(unique);
        }

        AuditSection(campaign, "customerGroups", before, groupIds.Order().ToList(), utcNow);
        await SaveGuardedAsync(campaign, cancellationToken);
        return await MapStoredAsync(campaign.Id, utcNow, cancellationToken);
    }

    /// <summary>
    /// Tags da campanha (spec GH-077, D-2 e D-3): em qualquer status, até 5, da rede da campanha e ativas. A tag inativada
    /// que a campanha já tem pode continuar na lista; só não pode ser aplicada de novo.
    /// </summary>
    public async Task<CampaignResponse> ReplaceTagsAsync(Guid id, ReplaceCampaignTagsRequest request, CancellationToken cancellationToken)
    {
        if (request.TagIds is not { } informed)
        {
            throw new RequestValidationException(nameof(request.TagIds), CampaignTagContract.TagIdsRequired);
        }

        if (informed.Any(tagId => tagId is null || tagId == Guid.Empty))
        {
            throw new RequestValidationException(nameof(request.TagIds), CampaignTagContract.TagIdInvalid);
        }

        var tagIds = informed.Select(tagId => tagId!.Value).Distinct().ToList();

        if (tagIds.Count > Campaign.MaxTags)
        {
            throw new RequestValidationException(nameof(request.TagIds), CampaignTagContract.TagIdsTooMany);
        }

        var campaign = await RequireExistingAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var found = (await campaignTags.FindManyAsync(tagIds, cancellationToken)).ToDictionary(tag => tag.Id);
        var current = campaign.TagLinks.Select(link => link.TagId).ToHashSet();

        RefuseTags(tagIds.Where(tagId => !found.ContainsKey(tagId)), CampaignErrors.TagNotFoundCode, CampaignErrors.TagNotFoundMessage);
        RefuseTags(tagIds.Where(tagId => found[tagId].NetworkId != campaign.NetworkId), CampaignErrors.TagOutsideNetworkCode, CampaignErrors.TagOutsideNetworkMessage);
        RefuseTags(tagIds.Where(tagId => !found[tagId].IsActive && !current.Contains(tagId)), CampaignErrors.TagInactiveCode, CampaignErrors.TagInactiveMessage);

        var before = campaign.TagLinks.Select(link => link.Tag?.Name ?? link.TagId.ToString()).Order(StringComparer.Ordinal).ToList();
        campaign.ReplaceTags(tagIds, utcNow);
        AuditSection(campaign, "tags", before, tagIds.Select(tagId => found[tagId].Name).Order(StringComparer.Ordinal).ToList(), utcNow);
        await SaveGuardedAsync(campaign, cancellationToken);

        return await MapStoredAsync(campaign.Id, utcNow, cancellationToken);
    }

    private static void RefuseTags(IEnumerable<Guid> refused, string code, string message)
    {
        var ids = refused.ToList();

        if (ids.Count > 0)
        {
            throw new BusinessRuleException(code, message, new Dictionary<string, object?> { ["tagIds"] = ids });
        }
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
        if (campaign.CustomerGroups.Count > 0)
        {
            var unique = await customerGroupStore.CountDistinctDocumentsAsync(
                campaign.CustomerGroups.Select(link => link.GroupId).ToList(), cancellationToken);
            campaign.EnsureCustomerGroupQuota(unique);
        }

        AuditSection(campaign, "quantities", before, SnapshotQuantities(campaign), utcNow);
        await SaveGuardedAsync(campaign, cancellationToken);

        return await RespondAsync(campaign, campaign.Network!, utcNow, cancellationToken);
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
        // Uma consulta só; a que não existe responde 422 com o campo, como alvos e condições (spec GH-075, D-1).
        var categoryIds = audience.Criteria.Where(criterion => criterion.CategoryId is not null).Select(criterion => criterion.CategoryId!.Value).Distinct().ToList();
        var known = categoryIds.Count == 0
            ? new HashSet<Guid>()
            : (await catalog.GetCategoriesByIdsAsync(categoryIds, cancellationToken)).Select(category => category.Id).ToHashSet();

        for (var index = 0; index < audience.Criteria.Count; index++)
        {
            if (audience.Criteria[index].CategoryId is { } categoryId && !known.Contains(categoryId))
            {
                throw new BusinessRuleException(
                    CampaignErrors.AudienceCategoryNotFoundCode,
                    EligibilityContract.CategoryNotFound,
                    new Dictionary<string, object?> { ["field"] = $"Criteria[{index}].CategoryId" });
            }
        }

        await campaigns.LoadEligibleCustomersAsync(campaign, cancellationToken);

        var before = SnapshotAudience(campaign);
        var changes = CustomerListChange.Between(campaign.EligibleCustomers.Select(customer => customer.Document), audience.Documents);
        campaign.SetAudience(audienceType, audience.Criteria, audience.Documents, utcNow);
        AuditAudience(campaign, before, SnapshotAudience(campaign, changes), utcNow);

        // Substituir a lista por diferença exige que ninguém a tenha mudado no meio (clique duplo no portal).
        await SaveGuardedAsync(campaign, cancellationToken);

        return await RespondAsync(campaign, campaign.Network!, utcNow, cancellationToken);
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

        return await RespondAsync(campaign, campaign.Network, utcNow, cancellationToken);
    }

    public async Task<CampaignResponse> UpdateVoucherValidityAsync(
        Guid id,
        UpdateVoucherValidityRequest request,
        CancellationToken cancellationToken)
    {
        // Só dias a partir da emissão (spec GH-066, D-1): data fixa e horas deixam de ser aceitas na gravação; as campanhas
        // já gravadas com elas continuam valendo como estão.
        var type = ParseEnum<VoucherValidityType>(request.Type, nameof(request.Type), CampaignContract.ValidityTypeInvalid);

        if (type != VoucherValidityType.Relative)
        {
            throw new RequestValidationException(nameof(request.Type), CampaignContract.ValidityOnlyDays);
        }

        var unit = ParseRelativeValidity(request);

        var campaign = await RequireForUpdateAsync(id, cancellationToken);
        var timeZone = NetworkTime.Resolve(campaign.Network!.TimeZone);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var before = MapValidity(campaign, timeZone);

        var cap = request.Cap is { } localCap ? NetworkTime.ToUtc(localCap, timeZone, nameof(request.Cap)) : (DateTime?)null;
        campaign.SetRelativeValidity(request.Amount!.Value, unit, cap, utcNow);

        AuditSection(campaign, "voucherValidity", before, MapValidity(campaign, timeZone), utcNow);
        await SaveGuardedAsync(campaign, cancellationToken);

        return await RespondAsync(campaign, campaign.Network, utcNow, cancellationToken);
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

        return await RespondAsync(campaign, campaign.Network!, utcNow, cancellationToken);
    }

    /// <summary>
    /// Alvos de resgate (spec GH-070, D-2): mesmo caminho dos alvos da concessão (contrato, códigos numa consulta,
    /// catálogo em lote), sem benefício nem cota.
    /// </summary>
    public async Task<CampaignResponse> ReplaceRedemptionTargetsAsync(
        Guid id,
        ReplaceRedemptionTargetsRequest request,
        CancellationToken cancellationToken)
    {
        var campaign = await RequireForUpdateAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow();
        var parsed = new List<(CampaignItem Item, string? ProductCode)>();

        for (var index = 0; index < request.Targets!.Count; index++)
        {
            parsed.Add(ParseRedemptionTarget(request.Targets[index], $"Targets[{index}]"));
        }

        await ResolveProductCodesAsync(parsed, cancellationToken);
        var probes = parsed.Select(entry => entry.Item).ToList();
        await EnsureTargetsInCatalogAsync(probes, cancellationToken);

        // Ids em sequência para os alvos voltarem na ordem informada.
        var targets = probes
            .Select((probe, index) => new CampaignRedemptionTarget
            {
                Id = Guid.CreateVersion7(utcNow.AddMilliseconds(index)),
                TargetType = probe.TargetType,
                CategoryId = probe.CategoryId,
                ProductId = probe.ProductId
            })
            .ToList();

        var before = SnapshotRedemptionTargets(campaign);
        campaign.ReplaceRedemptionTargets(targets, utcNow.UtcDateTime);
        AuditRedemptionTargets(campaign, before, SnapshotRedemptionTargets(campaign), utcNow.UtcDateTime);
        await SaveGuardedAsync(campaign, cancellationToken);

        // Relida para a resposta trazer o código de cada produto.
        return await MapStoredAsync(campaign.Id, utcNow.UtcDateTime, cancellationToken);
    }

    /// <summary>Janelas de resgate (spec GH-070, D-3), no fuso da rede.</summary>
    public async Task<CampaignResponse> ReplaceRedemptionWindowsAsync(
        Guid id,
        ReplaceRedemptionWindowsRequest request,
        CancellationToken cancellationToken)
    {
        var windows = request.Windows!.Select((window, index) => ParseRedemptionWindow(window, $"Windows[{index}]")).ToList();
        var campaign = await RequireForUpdateAsync(id, cancellationToken);
        var utcNow = timeProvider.GetUtcNow();

        for (var index = 0; index < windows.Count; index++)
        {
            windows[index].Id = Guid.CreateVersion7(utcNow.AddMilliseconds(index));
        }

        var before = SnapshotRedemptionWindows(campaign);
        campaign.ReplaceRedemptionWindows(windows, utcNow.UtcDateTime);
        AuditSection(campaign, "redemptionWindows", before, SnapshotRedemptionWindows(campaign), utcNow.UtcDateTime);
        await SaveGuardedAsync(campaign, cancellationToken);

        return await RespondAsync(campaign, campaign.Network!, utcNow.UtcDateTime, cancellationToken);
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

        await EnsureConditionsInCatalogAsync(conditions, cancellationToken);

        AuditSection(campaign, "conditions", before, SnapshotConditions(campaign), utcNow.UtcDateTime);
        await SaveGuardedAsync(campaign, cancellationToken);

        return await RespondAsync(campaign, campaign.Network!, utcNow.UtcDateTime, cancellationToken);
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

        if (unit != VoucherValidityUnit.Days)
        {
            throw new RequestValidationException(nameof(request.Unit), CampaignContract.ValidityOnlyDays);
        }

        if (request.Amount < 1 || request.Amount > CampaignContract.MaxValidityDays)
        {
            throw new RequestValidationException(nameof(request.Amount), CampaignContract.ValidityAmountRange);
        }

        return unit;
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

    /// <summary>
    /// Mesmas mensagens dos alvos (EP-02), com códigos próprios da condição (D-9). Uma consulta para as categorias e uma
    /// para os produtos (spec GH-071, D-4); a primeira condição com problema, na ordem informada, responde.
    /// </summary>
    private async Task EnsureConditionsInCatalogAsync(IReadOnlyList<CampaignCondition> conditions, CancellationToken cancellationToken)
    {
        var categoryIds = conditions.Where(condition => condition.CategoryId is not null).Select(condition => condition.CategoryId!.Value).Distinct().ToList();
        var productIds = conditions.Where(condition => condition.ProductId is not null).Select(condition => condition.ProductId!.Value).Distinct().ToList();

        var categories = categoryIds.Count == 0
            ? new Dictionary<Guid, Category>()
            : (await catalog.GetCategoriesByIdsAsync(categoryIds, cancellationToken)).ToDictionary(category => category.Id);
        var products = productIds.Count == 0
            ? new Dictionary<Guid, Product>()
            : (await catalog.GetProductsByIdsAsync(productIds, cancellationToken)).ToDictionary(product => product.Id);

        foreach (var condition in conditions)
        {
            EnsureConditionInCatalog(condition, categories, products);
        }
    }

    private static void EnsureConditionInCatalog(
        CampaignCondition condition, IReadOnlyDictionary<Guid, Category> categories, IReadOnlyDictionary<Guid, Product> products)
    {
        if (condition.CategoryId is { } categoryId)
        {
            var category = categories.GetValueOrDefault(categoryId)
                ?? throw new BusinessRuleException(CampaignErrors.ConditionCategoryNotFoundCode, CatalogErrors.ProductCategoryNotFoundMessage);

            if (!category.IsActive)
            {
                throw new BusinessRuleException(CampaignErrors.ConditionCategoryInactiveCode, CatalogErrors.ProductCategoryInactiveMessage);
            }
        }

        if (condition.ProductId is { } productId)
        {
            var product = products.GetValueOrDefault(productId)
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
        var json = AuditJson.Serialize(payload);

        // D-6 (GH-071): nenhuma seção pode derrubar a gravação por não caber na coluna da trilha (500 no PostgreSQL).
        // As seções grandes já têm resumo; o que ainda passar do limite vira só a seção e a marca de truncado.
        if (json.Length > AuditJson.MaxPayloadLength)
        {
            json = AuditJson.Serialize(new TruncatedPayload((payload as SectionChangePayload)?.Section, true));
        }

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
            Payload = json
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

    /// <summary>
    /// D-6 (GH-068): a lista inteira de alvos quando cabe na coluna da trilha; senão, um resumo de cada lado
    /// (quantidade, desconto, alvos por tipo, soma das cotas) e, no depois, quantos alvos entraram e saíram.
    /// </summary>
    private void AuditItems(Campaign campaign, List<ItemSnapshot> before, List<ItemSnapshot> after, DateTime utcNow)
    {
        if (before.SequenceEqual(after))
        {
            return;
        }

        if (AuditJson.Serialize(new SectionChangePayload("items", before, after)).Length <= AuditJson.MaxPayloadLength)
        {
            AuditSection(campaign, "items", before, after, utcNow);
            return;
        }

        var added = after.Except(before).Count();
        var removed = before.Except(after).Count();
        AuditSection(campaign, "items", ItemsSummary.Of(before, null), ItemsSummary.Of(after, new ItemsChange(added, removed)), utcNow);
    }

    /// <summary>
    /// D-6 (GH-071): a lista de locais inteira quando cabe; senão, quantos locais, se vale a rede inteira, quantas regiões
    /// e lojas, os canais e, no depois, quantos locais entraram e saíram.
    /// </summary>
    private void AuditScopes(Campaign campaign, List<ScopeSnapshot> before, List<ScopeSnapshot> after, DateTime utcNow)
    {
        const string section = "scopes";

        if (AuditJson.Serialize(new SectionChangePayload(section, before, after)).Length <= AuditJson.MaxPayloadLength)
        {
            AuditSection(campaign, section, before, after, utcNow);
            return;
        }

        var beforeKeys = before.Select(ScopesSummary.Key).ToHashSet();
        var afterKeys = after.Select(ScopesSummary.Key).ToHashSet();
        var changes = new ItemsChange(afterKeys.Count(key => !beforeKeys.Contains(key)), beforeKeys.Count(key => !afterKeys.Contains(key)));
        AuditSection(campaign, section, ScopesSummary.Of(before, null), ScopesSummary.Of(after, changes), utcNow);
    }

    /// <summary>D-6 (GH-071): o público inteiro quando cabe; senão, os critérios com a quantidade de UFs e municípios no lugar das listas.</summary>
    private void AuditAudience(Campaign campaign, AudienceSnapshot before, AudienceSnapshot after, DateTime utcNow)
    {
        const string section = "audience";

        if (AuditJson.Serialize(new SectionChangePayload(section, before, after)).Length <= AuditJson.MaxPayloadLength)
        {
            AuditSection(campaign, section, before, after, utcNow);
            return;
        }

        // Os resumos de antes e depois podem ter as mesmas quantidades: o que entrou e saiu mantém a troca na trilha.
        AuditSection(campaign, section, AudienceSummary.Of(before, null), AudienceSummary.Of(after, LocationChange.Between(before, after)), utcNow);
    }

    /// <summary>D-5 (GH-070): a lista inteira quando cabe na coluna da trilha; senão, quantos alvos de cada tipo e o que mudou.</summary>
    private void AuditRedemptionTargets(
        Campaign campaign, List<RedemptionTargetSnapshot> before, List<RedemptionTargetSnapshot> after, DateTime utcNow)
    {
        const string section = "redemptionTargets";

        if (before.SequenceEqual(after))
        {
            return;
        }

        if (AuditJson.Serialize(new SectionChangePayload(section, before, after)).Length <= AuditJson.MaxPayloadLength)
        {
            AuditSection(campaign, section, before, after, utcNow);
            return;
        }

        var changes = new ItemsChange(after.Except(before).Count(), before.Except(after).Count());
        AuditSection(campaign, section, RedemptionTargetsSummary.Of(before, null), RedemptionTargetsSummary.Of(after, changes), utcNow);
    }

    private static List<RedemptionTargetSnapshot> SnapshotRedemptionTargets(Campaign campaign) =>
        campaign.RedemptionTargets.OrderBy(target => target.Id)
            .Select(target => new RedemptionTargetSnapshot(target.TargetType.ToString(), target.CategoryId, target.ProductId))
            .ToList();

    private static List<RedemptionWindowSnapshot> SnapshotRedemptionWindows(Campaign campaign) =>
        campaign.RedemptionWindows.OrderBy(window => window.Id)
            .Select(window => new RedemptionWindowSnapshot(
                RedemptionRestrictionsMapper.Days(window),
                RedemptionRestrictionsMapper.Time(window.StartMinute),
                RedemptionRestrictionsMapper.Time(window.EndMinute)))
            .ToList();

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

    private sealed record ItemsChange(int Added, int Removed);

    /// <summary>Resumo da lista de alvos para a trilha quando a lista não cabe (D-6 da GH-068).</summary>
    private sealed record ItemsSummary(
        int Count,
        IReadOnlyList<string> BenefitTypes,
        IReadOnlyList<decimal> BenefitValues,
        IReadOnlyDictionary<string, int> Targets,
        int QuotaTotal,
        ItemsChange? Changes)
    {
        public static ItemsSummary Of(IReadOnlyList<ItemSnapshot> items, ItemsChange? changes) =>
            new(
                items.Count,
                items.Select(item => item.BenefitType).Distinct().Order(StringComparer.Ordinal).ToList(),
                items.Select(item => item.BenefitValue).Distinct().Order().ToList(),
                items.GroupBy(item => item.TargetType).OrderBy(group => group.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count()),
                items.Sum(item => item.VoucherLimit ?? 0),
                changes);
    }

    private sealed record RedemptionTargetSnapshot(string TargetType, Guid? CategoryId, Guid? ProductId);

    private sealed record RedemptionWindowSnapshot(IReadOnlyList<string> DaysOfWeek, string Start, string End);

    /// <summary>Resumo dos alvos de resgate para a trilha quando a lista não cabe (D-5 da GH-070).</summary>
    private sealed record RedemptionTargetsSummary(int Count, IReadOnlyDictionary<string, int> Targets, ItemsChange? Changes)
    {
        public static RedemptionTargetsSummary Of(IReadOnlyList<RedemptionTargetSnapshot> targets, ItemsChange? changes) =>
            new(
                targets.Count,
                targets.GroupBy(target => target.TargetType).OrderBy(group => group.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count()),
                changes);
    }

    private sealed record ConditionSnapshot(string Type, decimal? MinAmount, int? MinQuantity, Guid? CategoryId, Guid? ProductId);

    private sealed record ConditionsSnapshot(IReadOnlyList<ConditionSnapshot> Conditions, bool AllowUseInSamePurchase);

    private sealed record ScopeSnapshot(Guid? RegionId, Guid? EstablishmentId, IReadOnlyList<string> Channels);

    /// <summary>Resumo do escopo para a trilha quando a lista não cabe (D-6 da GH-071).</summary>
    private sealed record ScopesSummary(
        int Locations, bool Network, int Regions, int Establishments, IReadOnlyList<string> Channels, ItemsChange? Changes)
    {
        public static string Key(ScopeSnapshot scope) => $"{scope.RegionId}|{scope.EstablishmentId}|{string.Join(',', scope.Channels)}";

        public static ScopesSummary Of(IReadOnlyList<ScopeSnapshot> scopes, ItemsChange? changes) =>
            new(
                scopes.Count,
                scopes.Any(scope => scope.RegionId is null && scope.EstablishmentId is null),
                scopes.Count(scope => scope.RegionId is not null && scope.EstablishmentId is null),
                scopes.Count(scope => scope.EstablishmentId is not null),
                scopes.SelectMany(scope => scope.Channels).Distinct().Order(StringComparer.Ordinal).ToList(),
                changes);
    }

    /// <summary>Público para a trilha quando as listas de UFs e municípios não cabem (D-6 da GH-071).</summary>
    private sealed record AudienceSummary(
        string? AudienceType, IReadOnlyList<CriterionSummary> Criteria, CustomerListChange? CustomerChanges, LocationChange? LocationChanges)
    {
        public static AudienceSummary Of(AudienceSnapshot audience, LocationChange? locationChanges) =>
            new(
                audience.AudienceType,
                audience.Criteria.Select(criterion => new CriterionSummary(
                    criterion.Type,
                    criterion.MinAge,
                    criterion.MaxAge,
                    criterion.States?.Count,
                    criterion.CityCodes?.Count,
                    criterion.MinAmount,
                    criterion.MinPurchases,
                    criterion.CategoryId,
                    criterion.WindowDays,
                    criterion.CustomerCount)).ToList(),
                audience.CustomerChanges,
                locationChanges);
    }

    /// <summary>UFs e municípios que entraram e saíram dos critérios de localização (D-6 da GH-071).</summary>
    private sealed record LocationChange(int StatesAdded, int StatesRemoved, int CityCodesAdded, int CityCodesRemoved)
    {
        public static LocationChange Between(AudienceSnapshot before, AudienceSnapshot after)
        {
            var statesBefore = before.Criteria.SelectMany(criterion => criterion.States ?? []).ToHashSet(StringComparer.Ordinal);
            var statesAfter = after.Criteria.SelectMany(criterion => criterion.States ?? []).ToHashSet(StringComparer.Ordinal);
            var citiesBefore = before.Criteria.SelectMany(criterion => criterion.CityCodes ?? []).ToHashSet(StringComparer.Ordinal);
            var citiesAfter = after.Criteria.SelectMany(criterion => criterion.CityCodes ?? []).ToHashSet(StringComparer.Ordinal);

            return new LocationChange(
                statesAfter.Count(state => !statesBefore.Contains(state)),
                statesBefore.Count(state => !statesAfter.Contains(state)),
                citiesAfter.Count(city => !citiesBefore.Contains(city)),
                citiesBefore.Count(city => !citiesAfter.Contains(city)));
        }
    }

    private sealed record CriterionSummary(
        string Type,
        int? MinAge,
        int? MaxAge,
        int? StatesCount,
        int? CityCodesCount,
        decimal? MinAmount,
        int? MinPurchases,
        Guid? CategoryId,
        int? WindowDays,
        int? CustomerCount);

    private sealed record TruncatedPayload(string? Section, bool Truncated);

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

    /// <summary>
    /// Locais existentes, ativos e na rede da campanha; na loja, só canal que ela opera (PR-11). Uma consulta para as
    /// lojas (com a região) e uma para as regiões (spec GH-071, D-4); o primeiro local com problema, na ordem informada, responde.
    /// </summary>
    private async Task EnsureScopeLocationsAsync(
        Guid networkId,
        IReadOnlyList<(Guid? RegionId, Guid? EstablishmentId, IReadOnlyList<ChannelType> Channels)> entries,
        CancellationToken cancellationToken)
    {
        var establishmentIds = entries.Where(entry => entry.EstablishmentId is not null).Select(entry => entry.EstablishmentId!.Value).Distinct().ToList();
        var regionIds = entries.Where(entry => entry.EstablishmentId is null && entry.RegionId is not null).Select(entry => entry.RegionId!.Value).Distinct().ToList();

        var establishments = (await organization.GetEstablishmentsByIdsAsync(establishmentIds, cancellationToken)).ToDictionary(store => store.Id);
        var regions = (await organization.GetRegionsByIdsAsync(regionIds, cancellationToken)).ToDictionary(region => region.Id);

        foreach (var entry in entries)
        {
            EnsureScopeLocation(networkId, entry, establishments, regions);
        }
    }

    private static void EnsureScopeLocation(
        Guid networkId,
        (Guid? RegionId, Guid? EstablishmentId, IReadOnlyList<ChannelType> Channels) entry,
        IReadOnlyDictionary<Guid, Establishment> establishments,
        IReadOnlyDictionary<Guid, Region> regions)
    {
        if (entry.EstablishmentId is { } establishmentId)
        {
            var establishment = establishments.GetValueOrDefault(establishmentId)
                ?? throw new BusinessRuleException(
                    CampaignErrors.ScopeEstablishmentNotFoundCode, CampaignErrors.ScopeEstablishmentNotFoundMessage);

            if (establishment.Region?.NetworkId != networkId)
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
            var region = regions.GetValueOrDefault(regionId)
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

        return await RespondAsync(stored, stored.Network!, utcNow, cancellationToken);
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
    private static (CampaignItem Item, string? ProductCode) ParseItem(CampaignItemRequest? request, string field)
    {
        if (request is null)
        {
            throw new RequestValidationException(field, CampaignContract.TargetTypeRequired);
        }

        var targetType = ParseEnum<TargetType>(request.TargetType, $"{field}.TargetType", CampaignContract.TargetTypeInvalid);
        var benefitType = ParseEnum<BenefitType>(request.BenefitType, $"{field}.BenefitType", CampaignContract.BenefitTypeInvalid);

        var categoryId = NonEmpty(request.CategoryId);
        var productId = NonEmpty(request.ProductId);
        var productCode = string.IsNullOrWhiteSpace(request.ProductCode) ? null : request.ProductCode.Trim();

        // D-2 (GH-068): o produto vem pelo id ou pelo código do catálogo, nunca pelos dois.
        if (productCode is not null && targetType != TargetType.Product)
        {
            throw new RequestValidationException($"{field}.ProductCode", CampaignContract.TargetProductCodeOnlyForProduct);
        }

        if (productCode is not null && productId is not null)
        {
            throw new RequestValidationException($"{field}.ProductCode", CampaignContract.TargetProductIdAndCode);
        }

        switch (targetType)
        {
            case TargetType.All when categoryId is not null || productId is not null:
                throw new RequestValidationException($"{field}.TargetType", CampaignContract.TargetAllWithId);
            case TargetType.Category when categoryId is null || productId is not null:
                throw new RequestValidationException($"{field}.CategoryId", CampaignContract.TargetCategoryRequired);
            case TargetType.Product when (productId is null && productCode is null) || categoryId is not null:
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

        var item = new CampaignItem
        {
            TargetType = targetType,
            CategoryId = categoryId,
            ProductId = productId,
            BenefitType = benefitType,
            BenefitValue = benefitValue,
            VoucherLimit = request.VoucherLimit
        };

        return (item, productCode);
    }

    /// <summary>
    /// D-2 (GH-068): troca o código pelo id do produto, numa consulta só. Códigos que faltam ou estão inativos
    /// recusam a lista inteira, com os códigos em <c>codes</c> (como a importação em lote).
    /// </summary>
    private async Task ResolveProductCodesAsync(
        IReadOnlyList<(CampaignItem Item, string? ProductCode)> parsed, CancellationToken cancellationToken)
    {
        var codes = parsed.Where(entry => entry.ProductCode is not null).Select(entry => entry.ProductCode!).ToList();

        if (codes.Count == 0)
        {
            return;
        }

        var products = (await catalog.GetProductsByCodesAsync(codes, cancellationToken))
            .ToDictionary(product => product.Code, StringComparer.OrdinalIgnoreCase);

        var missing = codes.Where(code => !products.ContainsKey(code)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (missing.Count > 0)
        {
            throw new BusinessRuleException(
                CampaignErrors.ItemProductCodeNotFoundCode,
                CampaignErrors.ItemProductCodeNotFoundMessage,
                new Dictionary<string, object?> { ["codes"] = missing.Order(StringComparer.Ordinal).ToList() });
        }

        // O inativo existe no catálogo: devolve o código como está cadastrado, para o portal achar o produto.
        var inactive = codes.Select(code => products[code]).Where(product => !product.IsActive).Select(product => product.Code).Distinct().ToList();

        if (inactive.Count > 0)
        {
            throw new BusinessRuleException(
                CampaignErrors.ItemProductInactiveCode,
                CampaignErrors.ItemProductCodeInactiveMessage,
                new Dictionary<string, object?> { ["codes"] = inactive.Order(StringComparer.Ordinal).ToList() });
        }

        foreach (var (item, productCode) in parsed)
        {
            if (productCode is not null)
            {
                item.ProductId = products[productCode].Id;
            }
        }
    }

    /// <summary>Categoria e produto são globais (sem rede); basta existir e estar ativo.</summary>
    /// <remarks>D-3 (GH-068): uma consulta para as categorias e uma para os produtos; o primeiro alvo com problema responde.</remarks>
    private async Task EnsureTargetsInCatalogAsync(IReadOnlyList<CampaignItem> items, CancellationToken cancellationToken)
    {
        var categoryIds = items.Where(item => item.CategoryId is not null).Select(item => item.CategoryId!.Value).Distinct().ToList();
        var productIds = items.Where(item => item.ProductId is not null).Select(item => item.ProductId!.Value).Distinct().ToList();

        var categories = categoryIds.Count == 0
            ? new Dictionary<Guid, Category>()
            : (await catalog.GetCategoriesByIdsAsync(categoryIds, cancellationToken)).ToDictionary(category => category.Id);
        var products = productIds.Count == 0
            ? new Dictionary<Guid, Product>()
            : (await catalog.GetProductsByIdsAsync(productIds, cancellationToken)).ToDictionary(product => product.Id);

        foreach (var item in items)
        {
            if (item.CategoryId is { } categoryId)
            {
                var category = categories.GetValueOrDefault(categoryId)
                    ?? throw new BusinessRuleException(CampaignErrors.ItemCategoryNotFoundCode, CatalogErrors.ProductCategoryNotFoundMessage);

                if (!category.IsActive)
                {
                    throw new BusinessRuleException(CampaignErrors.ItemCategoryInactiveCode, CatalogErrors.ProductCategoryInactiveMessage);
                }
            }

            if (item.ProductId is { } productId)
            {
                var product = products.GetValueOrDefault(productId)
                    ?? throw new BusinessRuleException(CampaignErrors.ItemProductNotFoundCode, CampaignErrors.ItemProductNotFoundMessage);

                if (!product.IsActive)
                {
                    throw new BusinessRuleException(CampaignErrors.ItemProductInactiveCode, CampaignErrors.ItemProductInactiveMessage);
                }
            }
        }
    }

    /// <summary>Alvo de resgate (D-2 da GH-070): só categoria ou produto, com as mesmas regras de id e código dos alvos.</summary>
    private static (CampaignItem Item, string? ProductCode) ParseRedemptionTarget(RedemptionTargetRequest? request, string field)
    {
        if (request is null)
        {
            throw new RequestValidationException(field, CampaignContract.TargetTypeRequired);
        }

        var targetType = ParseEnum<TargetType>(request.TargetType, $"{field}.TargetType", CampaignContract.RedemptionTargetTypeInvalid);

        if (targetType == TargetType.All)
        {
            throw new RequestValidationException($"{field}.TargetType", CampaignContract.RedemptionTargetTypeInvalid);
        }

        var categoryId = NonEmpty(request.CategoryId);
        var productId = NonEmpty(request.ProductId);
        var productCode = string.IsNullOrWhiteSpace(request.ProductCode) ? null : request.ProductCode.Trim();

        if (productCode is not null && targetType != TargetType.Product)
        {
            throw new RequestValidationException($"{field}.ProductCode", CampaignContract.TargetProductCodeOnlyForProduct);
        }

        if (productCode is not null && productId is not null)
        {
            throw new RequestValidationException($"{field}.ProductCode", CampaignContract.TargetProductIdAndCode);
        }

        switch (targetType)
        {
            case TargetType.Category when categoryId is null || productId is not null:
                throw new RequestValidationException($"{field}.CategoryId", CampaignContract.TargetCategoryRequired);
            case TargetType.Product when (productId is null && productCode is null) || categoryId is not null:
                throw new RequestValidationException($"{field}.ProductId", CampaignContract.TargetProductRequired);
        }

        // O item só leva o alvo pela resolução de códigos e pela conferência do catálogo, as mesmas dos alvos da concessão.
        return (new CampaignItem { TargetType = targetType, CategoryId = categoryId, ProductId = productId }, productCode);
    }

    /// <summary>Janela de resgate (D-3 da GH-070): dias sem repetição e horários <c>HH:mm</c>.</summary>
    private static CampaignRedemptionWindow ParseRedemptionWindow(RedemptionWindowRequest? request, string field)
    {
        if (request?.DaysOfWeek is not { Count: > 0 } values)
        {
            throw new RequestValidationException($"{field}.DaysOfWeek", CampaignContract.RedemptionWindowDaysRequired);
        }

        var days = values
            .Select(value => ParseEnum<DayOfWeek>(value, $"{field}.DaysOfWeek", CampaignContract.RedemptionWindowDayInvalid))
            .ToList();

        if (days.Distinct().Count() != days.Count)
        {
            throw new RequestValidationException($"{field}.DaysOfWeek", CampaignContract.RedemptionWindowDayInvalid);
        }

        return new CampaignRedemptionWindow
        {
            DaysOfWeek = CampaignRedemptionWindow.MaskOf(days),
            StartMinute = ParseMinuteOfDay(request.Start, $"{field}.Start"),
            EndMinute = ParseMinuteOfDay(request.End, $"{field}.End")
        };
    }

    private static int ParseMinuteOfDay(string? value, string field)
    {
        if (!TimeOnly.TryParseExact(value?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            throw new RequestValidationException(field, CampaignContract.RedemptionWindowTimeInvalid);
        }

        return time.Hour * 60 + time.Minute;
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

    private readonly record struct CampaignVoucherCounters(int Redeemed, int Expired);

    private static readonly VoucherFilter NoVoucherFilter = new(null, null, null, null, null);

    /// <summary>
    /// Contadores de vouchers por campanha numa agregação só, no SQL e pela mesma regra do dashboard (spec GH-050, D-3):
    /// resgatados são os emitidos com ao menos uma utilização; expirados, os vencidos sem utilização.
    /// </summary>
    private async Task<Dictionary<Guid, CampaignVoucherCounters>> LoadCountersAsync(
        Guid networkId, Guid? campaignId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var counts = await voucherCounts.CountVouchersAsync(networkId, campaignId, NoVoucherFilter, utcNow, cancellationToken);

        return counts.ToDictionary(count => count.CampaignId, count => new CampaignVoucherCounters(count.Redeemed, count.ExpiredUnused));
    }

    /// <summary>Resposta de uma campanha já gravada, com os contadores de vouchers (as mudanças de status também os devolvem).</summary>
    private async Task<CampaignResponse> RespondAsync(Campaign campaign, Network network, DateTime utcNow, CancellationToken cancellationToken)
    {
        var counters = await LoadCountersAsync(campaign.NetworkId, campaign.Id, utcNow, cancellationToken);
        var uniques = await customerGroupStore.CountDistinctByCampaignAsync([campaign.Id], cancellationToken);

        return Map(campaign, network, utcNow, counters.GetValueOrDefault(campaign.Id), uniques.GetValueOrDefault(campaign.Id));
    }

    // Status efetivo no instante da leitura (PR-04). Contadores de resgate/expiração: GH-050.
    private static CampaignResponse Map(
        Campaign campaign, Network network, DateTime utcNow, CampaignVoucherCounters counters = default, int uniqueCustomers = 0)
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
            counters.Redeemed,
            counters.Expired,
            MapEligibility(campaign),
            RedemptionRestrictionsMapper.Targets(campaign),
            RedemptionRestrictionsMapper.Windows(campaign),
            campaign.CustomerGroups
                .OrderBy(link => link.Id)
                .Select(link => new CampaignCustomerGroupResponse(link.GroupId, link.Group?.Name ?? string.Empty))
                .ToList(),
            uniqueCustomers,
            campaign.TagLinks
                .Where(link => link.Tag is not null)
                .OrderBy(link => link.Tag!.NormalizedName, StringComparer.Ordinal)
                .Select(link => new CampaignTagRefResponse(link.TagId, link.Tag!.Name, link.Tag.IsActive))
                .ToList());
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
            item.VoucherLimit,
            item.Product?.Code);
}
