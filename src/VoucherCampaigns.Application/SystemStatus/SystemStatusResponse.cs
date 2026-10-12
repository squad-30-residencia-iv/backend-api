namespace VoucherCampaigns.Application.SystemStatus;

public record SystemStatusResponse(
    string Provider,
    bool CanConnect,
    IReadOnlyList<string> AppliedMigrations);
