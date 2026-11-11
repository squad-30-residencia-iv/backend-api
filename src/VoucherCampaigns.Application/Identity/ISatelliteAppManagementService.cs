namespace VoucherCampaigns.Application.Identity;

public interface ISatelliteAppManagementService
{
    Task<CreateSatelliteAppResponse> CreateAsync(CreateSatelliteAppRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<SatelliteAppItemResponse>> GetByNetworkAsync(Guid networkId, CancellationToken cancellationToken);
    Task<SatelliteAppItemResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<SatelliteAppItemResponse> UpdateAsync(Guid id, UpdateSatelliteAppRequest request, CancellationToken cancellationToken);
    Task<SatelliteAppItemResponse> DeactivateAsync(Guid id, CancellationToken cancellationToken);
    Task<RotateSecretResponse> RotateSecretAsync(Guid id, CancellationToken cancellationToken);
}
