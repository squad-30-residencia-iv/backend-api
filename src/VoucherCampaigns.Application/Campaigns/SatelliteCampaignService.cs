using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.Vouchers;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Campanha vigente como a aplicação satélite a vê (US-07.1, spec GH-049 D-14). Datas no fuso da rede; sem saldo.</summary>
/// <param name="Audience"><c>All</c> até o público restrito (#47).</param>
public record SatelliteCampaignResponse(
    Guid Id,
    string Name,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<VoucherTargetResponse> Items,
    IReadOnlyList<SatelliteConditionResponse> Conditions,
    SatelliteScopeResponse Scope,
    string Audience,
    bool AllowUseInSamePurchase,
    decimal? MinPurchaseAmount,
    decimal? MaxPurchaseAmount,
    int? MaxUsesPerVoucher);

public record SatelliteConditionResponse(string Type, decimal? MinAmount, int? MinQuantity, Guid? CategoryId, Guid? ProductId);

/// <param name="Network">Verdadeiro quando alguma linha vale para a rede inteira.</param>
/// <param name="Entries">Regiões e lojas com os canais de cada uma (a rede inteira aparece com os dois ids nulos).</param>
public record SatelliteScopeResponse(bool Network, IReadOnlyList<SatelliteScopeEntryResponse> Entries);

public record SatelliteScopeEntryResponse(Guid? RegionId, Guid? EstablishmentId, IReadOnlyList<string> Channels);

public interface ISatelliteCampaignService
{
    /// <summary>Campanhas efetivamente ativas da rede da aplicação, filtradas por loja e canal quando informados.</summary>
    Task<IReadOnlyList<SatelliteCampaignResponse>> GetActiveAsync(Guid? establishmentId, string? channel, CancellationToken cancellationToken);
}

public class SatelliteCampaignService(IConcessionRepository concessions, ICurrentSatelliteApp app, TimeProvider timeProvider) : ISatelliteCampaignService
{
    public async Task<IReadOnlyList<SatelliteCampaignResponse>> GetActiveAsync(
        Guid? establishmentId, string? channel, CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var channelFilter = string.IsNullOrWhiteSpace(channel) ? (ChannelType?)null : PurchaseParsing.ParseChannel(channel, "channel");
        Establishment? store = null;

        if (establishmentId is { } storeId)
        {
            store = await concessions.FindEstablishmentAsync(storeId, app.NetworkId, cancellationToken)
                ?? throw new ResourceNotFoundException(ConcessionContract.EstablishmentNotFoundForList);
        }

        var campaigns = await concessions.GetApprovedCampaignsAsync(app.NetworkId, cancellationToken);

        return campaigns
            .Where(campaign => campaign.EffectiveStatus(utcNow) == CampaignStatus.Active)
            .Where(campaign => InFilter(campaign, store, channelFilter))
            .OrderBy(campaign => campaign.EndsAt)
            .ThenBy(campaign => campaign.Name)
            .ThenBy(campaign => campaign.Id)
            .Select(Map)
            .ToList();
    }

    private static bool InFilter(Campaign campaign, Establishment? store, ChannelType? channel) =>
        (store, channel) switch
        {
            (null, null) => true,
            (null, { } onlyChannel) => campaign.Scopes.Any(scope => scope.ChannelType == onlyChannel),
            ({ } onlyStore, null) => campaign.IsInScope(onlyStore, ChannelType.Physical) || campaign.IsInScope(onlyStore, ChannelType.Virtual),
            ({ } someStore, { } someChannel) => campaign.IsInScope(someStore, someChannel)
        };

    private static SatelliteCampaignResponse Map(Campaign campaign)
    {
        var timeZone = VoucherMapper.TimeZoneOf(campaign);
        var entries = campaign.Scopes
            .OrderBy(scope => scope.Id)
            .GroupBy(scope => (scope.RegionId, scope.EstablishmentId))
            .Select(location => new SatelliteScopeEntryResponse(
                location.Key.RegionId,
                location.Key.EstablishmentId,
                location.Select(scope => scope.ChannelType).Distinct().Order().Select(scopeChannel => scopeChannel.ToString()).ToList()))
            .ToList();

        return new SatelliteCampaignResponse(
            campaign.Id,
            campaign.Name,
            NetworkTime.ToLocal(campaign.StartsAt, timeZone),
            NetworkTime.ToLocal(campaign.EndsAt, timeZone),
            campaign.Items.OrderBy(item => item.Id)
                .Select(item => new VoucherTargetResponse(item.TargetType.ToString(), item.CategoryId, item.ProductId, item.BenefitType.ToString(), item.BenefitValue))
                .ToList(),
            campaign.Conditions.OrderBy(condition => condition.Id)
                .Select(condition => new SatelliteConditionResponse(condition.Type.ToString(), condition.MinAmount, condition.MinQuantity, condition.CategoryId, condition.ProductId))
                .ToList(),
            new SatelliteScopeResponse(entries.Any(entry => entry.RegionId is null && entry.EstablishmentId is null), entries),
            "All",
            campaign.AllowUseInSamePurchase,
            campaign.MinPurchaseAmount,
            campaign.MaxPurchaseAmount,
            campaign.MaxUsesPerVoucher);
    }
}
