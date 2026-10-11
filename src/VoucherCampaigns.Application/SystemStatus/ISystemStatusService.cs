namespace VoucherCampaigns.Application.SystemStatus;

public interface ISystemStatusService
{
    Task<SystemStatusResponse> GetStatusAsync(CancellationToken cancellationToken);
}
