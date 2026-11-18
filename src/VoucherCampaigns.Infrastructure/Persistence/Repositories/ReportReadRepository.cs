using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Reports;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Infrastructure.Persistence.Repositories;

public class ReportReadRepository(AppDbContext context) : IReportReadRepository
{
    public async Task<IReadOnlyList<ReportVoucherRow>> ListVouchersAsync(Guid networkId, CancellationToken cancellationToken)
    {
        var rows = await (
            from voucher in context.Vouchers.AsNoTracking()
            join campaign in context.Campaigns.AsNoTracking() on voucher.CampaignId equals campaign.Id
            join establishment in context.Establishments.AsNoTracking() on voucher.EstablishmentId equals establishment.Id
            join region in context.Regions.AsNoTracking() on establishment.RegionId equals region.Id
            where voucher.NetworkId == networkId
            select new ReportVoucherRow(
                voucher.Id,
                campaign.Id,
                campaign.Name,
                establishment.Id,
                region.Id,
                region.Name,
                voucher.IssuedAt,
                voucher.Status,
                voucher.UsesCount,
                voucher.ValidUntil,
                voucher.ReservedUntil)
            ).ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<IReadOnlyList<ReportOptionResponse>> ListCampaignsAsync(Guid networkId, CancellationToken cancellationToken) =>
        await context.Campaigns.AsNoTracking()
            .Where(campaign => campaign.NetworkId == networkId)
            .OrderBy(campaign => campaign.Name)
            .Select(campaign => new ReportOptionResponse(campaign.Id, campaign.Name))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReportOptionResponse>> ListRegionsAsync(Guid networkId, CancellationToken cancellationToken) =>
        await context.Regions.AsNoTracking()
            .Where(region => region.NetworkId == networkId && region.IsActive)
            .OrderBy(region => region.Name)
            .Select(region => new ReportOptionResponse(region.Id, region.Name))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReportEstablishmentOptionResponse>> ListEstablishmentsAsync(
        Guid networkId, CancellationToken cancellationToken) =>
        await context.Establishments.AsNoTracking()
            .Where(establishment => establishment.Region!.NetworkId == networkId && establishment.IsActive)
            .OrderBy(establishment => establishment.Name)
            .Select(establishment => new ReportEstablishmentOptionResponse(
                establishment.Id, establishment.Name, establishment.RegionId))
            .ToListAsync(cancellationToken);
}
