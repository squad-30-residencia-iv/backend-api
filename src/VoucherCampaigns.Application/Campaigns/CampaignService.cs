using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;

namespace VoucherCampaigns.Application.Campaigns;

public class CampaignService(
    INetworkRepository networks,
    ICampaignRepository campaigns,
    TimeProvider timeProvider) : ICampaignService
{
    public async Task<CampaignResponse> CreateAsync(CreateCampaignRequest request, CancellationToken cancellationToken)
    {
        var network = await networks.FindByIdAsync(request.NetworkId!.Value, cancellationToken);

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

        return new CampaignResponse(
            campaign.Id,
            campaign.NetworkId,
            campaign.Name,
            campaign.Description,
            NetworkTime.ToLocal(campaign.StartsAt, timeZone),
            NetworkTime.ToLocal(campaign.EndsAt, timeZone),
            network.TimeZone,
            campaign.Status.ToString(),
            campaign.CreatedAt);
    }
}
