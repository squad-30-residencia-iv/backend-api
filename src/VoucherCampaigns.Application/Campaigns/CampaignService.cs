using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Campaigns;

public class CampaignService(
    INetworkRepository networks,
    ICampaignRepository campaigns,
    ICatalogRepository catalog,
    ICurrentUser currentUser,
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

        var campaign = Campaign.CreateDraft(
            network.Id,
            request.Name,
            request.Description,
            NetworkTime.ToUtc(request.StartsAt!.Value, timeZone, nameof(request.StartsAt)),
            NetworkTime.ToUtc(request.EndsAt!.Value, timeZone, nameof(request.EndsAt)),
            timeProvider.GetUtcNow().UtcDateTime);

        await campaigns.AddAsync(campaign, cancellationToken);

        return Map(campaign, network);
    }

    public async Task<IReadOnlyList<CampaignResponse>> GetAllAsync(Guid? networkId, string? status, CancellationToken cancellationToken)
    {
        var scopedNetworkId = InputValues.RequiredId(networkId, "NetworkId", CampaignContract.NetworkRequired);
        NetworkScope.Ensure(currentUser, scopedNetworkId);

        var statusFilter = ParseStatus(status);

        // Rede inativa continua consultável: é só leitura.
        var network = await networks.FindByIdAsync(scopedNetworkId, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.NetworkNotFound);

        var items = await campaigns.GetByNetworkAsync(scopedNetworkId, statusFilter, cancellationToken);
        return items.Select(campaign => Map(campaign, network)).ToList();
    }

    public async Task<CampaignResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await campaigns.FindByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(CampaignContract.CampaignNotFound);

        // A rede só é conhecida depois de carregar a campanha (US-02.5, cenário 3).
        NetworkScope.Ensure(currentUser, campaign.NetworkId);

        return Map(campaign, campaign.Network!);
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

        return Map(campaign, campaign.Network!);
    }

    public async Task<CampaignResponse> UpdateQuantitiesAsync(
        Guid id,
        UpdateCampaignQuantitiesRequest request,
        CancellationToken cancellationToken)
    {
        var campaign = await RequireForUpdateAsync(id, cancellationToken);

        campaign.SetQuantities(
            request.TotalVoucherLimit!.Value, request.PerCustomerVoucherLimit, timeProvider.GetUtcNow().UtcDateTime);
        await campaigns.SaveChangesAsync(cancellationToken);

        return Map(campaign, campaign.Network!);
    }

    public async Task<CampaignResponse> UpdateAudienceAsync(
        Guid id,
        UpdateCampaignAudienceRequest request,
        CancellationToken cancellationToken)
    {
        var audienceType = ParseEnum<CampaignAudienceType>(
            request.AudienceType, nameof(request.AudienceType), CampaignContract.AudienceTypeInvalid);
        var campaign = await RequireForUpdateAsync(id, cancellationToken);

        campaign.SetAudience(audienceType, timeProvider.GetUtcNow().UtcDateTime);
        await campaigns.SaveChangesAsync(cancellationToken);

        return Map(campaign, campaign.Network!);
    }

    public async Task<CampaignResponse> UpdateAsync(Guid id, UpdateCampaignRequest request, CancellationToken cancellationToken)
    {
        var campaign = await RequireForUpdateAsync(id, cancellationToken);

        // RN02: as datas digitadas valem no fuso da rede (US-02.6).
        var timeZone = NetworkTime.Resolve(campaign.Network!.TimeZone);

        campaign.UpdateDetails(
            request.Name,
            request.Description,
            NetworkTime.ToUtc(request.StartsAt!.Value, timeZone, nameof(request.StartsAt)),
            NetworkTime.ToUtc(request.EndsAt!.Value, timeZone, nameof(request.EndsAt)),
            timeProvider.GetUtcNow().UtcDateTime);
        await campaigns.SaveChangesAsync(cancellationToken);

        return Map(campaign, campaign.Network);
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

        return Map(campaign, campaign.Network);
    }

    public async Task<CampaignResponse> UpdateUsageRestrictionsAsync(
        Guid id,
        UpdateUsageRestrictionsRequest request,
        CancellationToken cancellationToken)
    {
        EnsurePurchaseAmount(request.MinPurchaseAmount, nameof(request.MinPurchaseAmount));
        EnsurePurchaseAmount(request.MaxPurchaseAmount, nameof(request.MaxPurchaseAmount));

        var campaign = await RequireForUpdateAsync(id, cancellationToken);

        campaign.SetUsageRestrictions(
            request.MinPurchaseAmount,
            request.MaxPurchaseAmount,
            request.MaxUsesPerVoucher!.Value,
            timeProvider.GetUtcNow().UtcDateTime);
        await campaigns.SaveChangesAsync(cancellationToken);

        return Map(campaign, campaign.Network!);
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

        return Map(campaign, campaign.Network!);
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

    /// <summary>Campanha existente (404), da rede do usuário (403) e em rascunho (422), nessa ordem.</summary>
    private async Task<Campaign> RequireForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await campaigns.FindForUpdateAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(CampaignContract.CampaignNotFound);

        NetworkScope.Ensure(currentUser, campaign.NetworkId);
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

    // Status gravado e IssuedVouchers = 0 até existirem o status efetivo e a emissão (spec US-01.9, PA-2 a PA-4).
    private static CampaignResponse Map(Campaign campaign, Network network)
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
            campaign.Status.ToString(),
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
            campaign.AllowUseInSamePurchase);
    }

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
