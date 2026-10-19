namespace VoucherCampaigns.Application.Campaigns;

public interface ICampaignService
{
    Task<CampaignResponse> CreateAsync(CreateCampaignRequest request, CancellationToken cancellationToken);
}
