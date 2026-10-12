namespace VoucherCampaigns.Application.SystemStatus;

public interface IDatabaseStatusReader
{
    Task<SystemStatusResponse> ReadAsync(CancellationToken cancellationToken);
}
