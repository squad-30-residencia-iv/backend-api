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
            DateTime.SpecifyKind(campaign.CreatedAt, DateTimeKind.Utc));
    }

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
