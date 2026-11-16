namespace VoucherCampaigns.Application.Reports;

public interface IReportService
{
    Task<ReportOverviewResponse> GetOverviewAsync(
        Guid? networkId,
        string? period,
        Guid? campaignId,
        Guid? regionId,
        Guid? establishmentId,
        CancellationToken cancellationToken);

    Task<ReportCatalogResponse> GetCatalogAsync(Guid? networkId, CancellationToken cancellationToken);
}
