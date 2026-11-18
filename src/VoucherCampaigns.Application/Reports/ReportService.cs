using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.Dashboard;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Domain.Common;

namespace VoucherCampaigns.Application.Reports;

public class ReportService(
    IReportReadRepository reports,
    INetworkRepository networks,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IReportService
{
    public async Task<ReportOverviewResponse> GetOverviewAsync(
        Guid? networkId,
        string? period,
        Guid? campaignId,
        Guid? regionId,
        Guid? establishmentId,
        CancellationToken cancellationToken)
    {
        var scopedNetworkId = InputValues.RequiredId(networkId, "NetworkId", ReportContract.NetworkRequired);
        NetworkScope.Ensure(currentUser, scopedNetworkId);

        var network = await networks.FindByIdAsync(scopedNetworkId, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.NetworkNotFound);

        var days = ParsePeriod(period);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var timeZone = NetworkTime.Resolve(network.TimeZone);
        var periodStartLocal = timeProvider.GetUtcNow().ToOffset(timeZone.GetUtcOffset(utcNow)).DateTime.Date.AddDays(-(days - 1));
        var periodStartUtc = NetworkTime.ToUtc(periodStartLocal, timeZone, "period");

        // Mesma regra do dashboard (spec GH-050, D-3): emitidos sem o provisório; resgatados com ao menos uma utilização;
        // vencidos e cancelados só sem utilização. A soma das fatias dá os emitidos.
        var rows = await reports.ListVouchersAsync(scopedNetworkId, cancellationToken);
        var inScope = rows
            .Select(row => (Row: row, Status: VoucherBuckets.Of(row.Status, row.UsesCount, row.ValidUntil, row.ReservedUntil, utcNow)))
            .Where(entry => entry.Status.IsIssued())
            .Where(entry => campaignId is null || entry.Row.CampaignId == campaignId)
            .Where(entry => regionId is null || entry.Row.RegionId == regionId)
            .Where(entry => establishmentId is null || entry.Row.EstablishmentId == establishmentId)
            .ToList();
        var filtered = inScope.Where(entry => entry.Row.IssuedAt >= periodStartUtc).ToList();

        var issued = filtered.Count;
        var redeemed = filtered.Count(entry => entry.Status == VoucherBucket.Redeemed);
        var expired = filtered.Count(entry => entry.Status == VoucherBucket.ExpiredUnused);
        var available = filtered.Count(entry => entry.Status == VoucherBucket.Available);
        var cancelled = filtered.Count(entry => entry.Status == VoucherBucket.CancelledUnused);
        decimal? rate = issued == 0 ? null : Math.Round(100m * redeemed / issued, 1, MidpointRounding.AwayFromZero);

        // O gráfico mensal cobre os últimos 6 meses, fora do recorte do período (que vale para os indicadores).
        var monthly = BuildMonthly(inScope, utcNow, timeZone);
        var distribution = new[]
            {
                new ReportStatusSliceResponse("redeemed", redeemed),
                new ReportStatusSliceResponse("available", available),
                new ReportStatusSliceResponse("expired", expired),
                new ReportStatusSliceResponse("cancelled", cancelled)
            }
            .Where(slice => slice.Count > 0)
            .ToList();

        var topCampaigns = filtered
            .GroupBy(entry => (entry.Row.CampaignId, entry.Row.CampaignName))
            .Select(group =>
            {
                var groupIssued = group.Count();
                var groupRedeemed = group.Count(entry => entry.Status == VoucherBucket.Redeemed);
                return new ReportCampaignRowResponse(
                    group.Key.CampaignId,
                    group.Key.CampaignName,
                    groupIssued,
                    groupRedeemed,
                    groupIssued == 0 ? null : Math.Round(100m * groupRedeemed / groupIssued, 1, MidpointRounding.AwayFromZero));
            })
            .OrderByDescending(row => row.Redeemed)
            .ThenByDescending(row => row.Issued)
            .Take(5)
            .ToList();

        var regions = filtered
            .GroupBy(entry => (entry.Row.RegionId, entry.Row.RegionName))
            .Select(group =>
            {
                var groupIssued = group.Count();
                var groupRedeemed = group.Count(entry => entry.Status == VoucherBucket.Redeemed);
                return new ReportRegionRowResponse(
                    group.Key.RegionId,
                    group.Key.RegionName,
                    groupIssued,
                    groupIssued == 0 ? null : Math.Round(100m * groupRedeemed / groupIssued, 1, MidpointRounding.AwayFromZero),
                    AverageTicket: null);
            })
            .OrderByDescending(row => row.Vouchers)
            .ToList();

        return new ReportOverviewResponse(
            new ReportKpisResponse(issued, redeemed, expired, available, cancelled, rate),
            monthly,
            distribution,
            topCampaigns,
            regions);
    }

    public async Task<ReportCatalogResponse> GetCatalogAsync(Guid? networkId, CancellationToken cancellationToken)
    {
        var scopedNetworkId = InputValues.RequiredId(networkId, "NetworkId", ReportContract.NetworkRequired);
        NetworkScope.Ensure(currentUser, scopedNetworkId);

        _ = await networks.FindByIdAsync(scopedNetworkId, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.NetworkNotFound);

        return new ReportCatalogResponse(
            await reports.ListCampaignsAsync(scopedNetworkId, cancellationToken),
            await reports.ListRegionsAsync(scopedNetworkId, cancellationToken),
            await reports.ListEstablishmentsAsync(scopedNetworkId, cancellationToken));
    }

    private static int ParsePeriod(string? period) =>
        period switch
        {
            null or "" or "30d" => 30,
            "90d" => 90,
            "6m" => 180,
            _ => throw new RequestValidationException("period", ReportContract.PeriodInvalid)
        };

    private static IReadOnlyList<ReportMonthlyPointResponse> BuildMonthly(
        IReadOnlyList<(ReportVoucherRow Row, VoucherBucket Status)> filtered,
        DateTime utcNow,
        TimeZoneInfo timeZone)
    {
        var points = new List<ReportMonthlyPointResponse>();
        var localNow = NetworkTime.ToLocal(utcNow, timeZone);

        for (var offset = 5; offset >= 0; offset--)
        {
            var month = new DateTime(localNow.Year, localNow.Month, 1).AddMonths(-offset);
            var label = month.ToString("MMM/yy", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
            var monthRows = filtered.Where(entry =>
            {
                var issuedLocal = NetworkTime.ToLocal(entry.Row.IssuedAt, timeZone);
                return issuedLocal.Year == month.Year && issuedLocal.Month == month.Month;
            }).ToList();

            points.Add(new ReportMonthlyPointResponse(
                label,
                monthRows.Count,
                monthRows.Count(entry => entry.Status == VoucherBucket.Redeemed),
                monthRows.Count(entry => entry.Status == VoucherBucket.ExpiredUnused)));
        }

        return points;
    }
}
