using VoucherCampaigns.Application.Eligibility;

namespace VoucherCampaigns.Application.Campaigns;

public interface ICampaignService
{
    Task<CampaignResponse> CreateAsync(CreateCampaignRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<CampaignResponse>> GetAllAsync(Guid? networkId, string? status, CancellationToken cancellationToken);

    Task<CampaignResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<CampaignResponse> ReplaceItemsAsync(Guid id, ReplaceCampaignItemsRequest request, CancellationToken cancellationToken);

    Task<CampaignResponse> UpdateQuantitiesAsync(Guid id, UpdateCampaignQuantitiesRequest request, CancellationToken cancellationToken);

    Task<CampaignResponse> UpdateAudienceAsync(Guid id, UpdateCampaignAudienceRequest request, CancellationToken cancellationToken);

    /// <summary>Lista de clientes específicos do público restrito, paginada (spec GH-047, D-10).</summary>
    Task<EligibleCustomersResponse> GetEligibleCustomersAsync(Guid id, int? page, int? pageSize, CancellationToken cancellationToken);

    /// <summary>Avalia o público para um cliente informado, sem emitir nem gravar (US-06.6, D-17).</summary>
    Task<EligibilitySimulationResponse> SimulateEligibilityAsync(Guid id, EligibilitySimulationRequest request, CancellationToken cancellationToken);

    Task<CampaignResponse> UpdateAsync(Guid id, UpdateCampaignRequest request, CancellationToken cancellationToken);

    Task<CampaignResponse> UpdateVoucherValidityAsync(Guid id, UpdateVoucherValidityRequest request, CancellationToken cancellationToken);

    Task<CampaignResponse> UpdateUsageRestrictionsAsync(Guid id, UpdateUsageRestrictionsRequest request, CancellationToken cancellationToken);

    Task<CampaignResponse> ReplaceConditionsAsync(Guid id, ReplaceCampaignConditionsRequest request, CancellationToken cancellationToken);

    Task<CampaignResponse> SubmitAsync(Guid id, CancellationToken cancellationToken);

    Task<CampaignResponse> ApproveAsync(Guid id, CancellationToken cancellationToken);

    Task<CampaignResponse> RejectAsync(Guid id, RejectCampaignRequest? request, CancellationToken cancellationToken);

    Task<CampaignResponse> ReplaceScopesAsync(Guid id, ReplaceCampaignScopesRequest request, CancellationToken cancellationToken);

    Task<CampaignResponse> DuplicateAsync(Guid id, DuplicateCampaignRequest? request, CancellationToken cancellationToken);

    Task<CampaignResponse> PauseAsync(Guid id, CancellationToken cancellationToken);

    Task<CampaignResponse> EndAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Cancela a campanha e invalida os vouchers não utilizados (US-01.8, RN10; spec GH-061).</summary>
    Task<CampaignResponse> CancelAsync(Guid id, CancelCampaignRequest request, CancellationToken cancellationToken);
}
