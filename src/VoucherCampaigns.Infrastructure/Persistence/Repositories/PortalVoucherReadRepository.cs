using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Vouchers;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Infrastructure.Persistence.Repositories;

public class PortalVoucherReadRepository(AppDbContext context) : IPortalVoucherReadRepository
{
    public async Task<IReadOnlyList<Voucher>> ListByNetworkAsync(
        Guid networkId, Guid? campaignId, CancellationToken cancellationToken)
    {
        var query = context.Vouchers.AsNoTracking()
            .Where(voucher => voucher.NetworkId == networkId);

        if (campaignId is { } id)
        {
            query = query.Where(voucher => voucher.CampaignId == id);
        }

        return await query
            .Include(voucher => voucher.Campaign!).ThenInclude(campaign => campaign.Network)
            .Include(voucher => voucher.Item)
            .Include(voucher => voucher.Concession)
            .OrderByDescending(voucher => voucher.IssuedAt)
            .ThenByDescending(voucher => voucher.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<Voucher?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Vouchers.AsNoTracking()
            .Include(voucher => voucher.Campaign!).ThenInclude(campaign => campaign.Network)
            .Include(voucher => voucher.Item)
            .Include(voucher => voucher.Concession)
            .Include(voucher => voucher.Events)
            .FirstOrDefaultAsync(voucher => voucher.Id == id, cancellationToken);

    public Task<Establishment?> FindEstablishmentAsync(Guid establishmentId, CancellationToken cancellationToken) =>
        context.Establishments.AsNoTracking()
            .FirstOrDefaultAsync(establishment => establishment.Id == establishmentId, cancellationToken);

    public async Task<IReadOnlyList<Voucher>> ListForCampaignsAsync(
        Guid networkId, IReadOnlyCollection<Guid> campaignIds, CancellationToken cancellationToken)
    {
        if (campaignIds.Count == 0)
        {
            return [];
        }

        return await context.Vouchers.AsNoTracking()
            .Where(voucher => voucher.NetworkId == networkId && campaignIds.Contains(voucher.CampaignId))
            .ToListAsync(cancellationToken);
    }
}
