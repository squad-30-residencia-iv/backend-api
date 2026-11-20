namespace VoucherCampaigns.Application.Reports;

public record ReportOverviewResponse(
    ReportKpisResponse Kpis,
    IReadOnlyList<ReportMonthlyPointResponse> Monthly,
    IReadOnlyList<ReportStatusSliceResponse> StatusDistribution,
    IReadOnlyList<ReportCampaignRowResponse> TopCampaigns,
    IReadOnlyList<ReportRegionRowResponse> Regions);

public record ReportKpisResponse(
    int Issued,
    int Redeemed,
    int Expired,
    int Available,
    int Cancelled,
    decimal? UtilizationRate);

public record ReportMonthlyPointResponse(string Month, int Issued, int Redeemed, int Expired);

public record ReportStatusSliceResponse(string Status, int Count);

public record ReportCampaignRowResponse(Guid CampaignId, string Name, int Issued, int Redeemed, decimal? Rate);

public record ReportRegionRowResponse(Guid RegionId, string Name, int Vouchers, decimal? Rate, decimal? AverageTicket);

public record ReportCatalogResponse(
    IReadOnlyList<ReportOptionResponse> Campaigns,
    IReadOnlyList<ReportOptionResponse> Regions,
    IReadOnlyList<ReportEstablishmentOptionResponse> Establishments);

public record ReportOptionResponse(Guid Value, string Label);

public record ReportEstablishmentOptionResponse(Guid Value, string Label, Guid RegionId);

public static class ReportContract
{
    public const string NetworkRequired = "Informe a rede.";
    public const string PeriodInvalid = "Período inválido. Use 30d, 90d ou 6m.";
}
