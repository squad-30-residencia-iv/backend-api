using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Infrastructure.Persistence.Repositories;

public class CampaignRepository(AppDbContext context) : ICampaignRepository
{
    public async Task AddAsync(Campaign campaign, CancellationToken cancellationToken)
    {
        context.Campaigns.Add(campaign);
        await context.SaveChangesAsync(cancellationToken);
    }
}
