namespace VoucherCampaigns.Application.Campaigns;

public interface ICampaignService
{
    Task<CampaignResponse> CreateAsync(CreateCampaignRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<CampaignResponse>> GetAllAsync(Guid? networkId, string? status, CancellationToken cancellationToken);

    Task<CampaignResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
