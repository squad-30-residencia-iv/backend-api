namespace VoucherCampaigns.Application.SystemStatus;

public class SystemStatusService(IDatabaseStatusReader databaseStatusReader) : ISystemStatusService
{
    public Task<SystemStatusResponse> GetStatusAsync(CancellationToken cancellationToken) =>
        databaseStatusReader.ReadAsync(cancellationToken);
}
