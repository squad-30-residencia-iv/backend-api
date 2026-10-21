using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Application.Campaigns;

public interface ICampaignRepository
{
    Task AddAsync(Campaign campaign, CancellationToken cancellationToken);
}
