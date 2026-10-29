namespace VoucherCampaigns.Application.Campaigns;

public interface ICampaignService
{
    Task<CampaignResponse> CreateAsync(CreateCampaignRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<CampaignResponse>> GetAllAsync(Guid? networkId, string? status, CancellationToken cancellationToken);

    Task<CampaignResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<CampaignResponse> ReplaceItemsAsync(Guid id, ReplaceCampaignItemsRequest request, CancellationToken cancellationToken);

    Task<CampaignResponse> UpdateQuantitiesAsync(Guid id, UpdateCampaignQuantitiesRequest request, CancellationToken cancellationToken);

    Task<CampaignResponse> UpdateAudienceAsync(Guid id, UpdateCampaignAudienceRequest request, CancellationToken cancellationToken);

    Task<CampaignResponse> UpdateAsync(Guid id, UpdateCampaignRequest request, CancellationToken cancellationToken);

    Task<CampaignResponse> UpdateVoucherValidityAsync(Guid id, UpdateVoucherValidityRequest request, CancellationToken cancellationToken);

    Task<CampaignResponse> UpdateUsageRestrictionsAsync(Guid id, UpdateUsageRestrictionsRequest request, CancellationToken cancellationToken);

    Task<CampaignResponse> ReplaceConditionsAsync(Guid id, ReplaceCampaignConditionsRequest request, CancellationToken cancellationToken);
}
