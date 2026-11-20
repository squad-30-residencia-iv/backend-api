using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Vouchers;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Infrastructure.Persistence.Repositories;

public class PortalVoucherReadRepository(AppDbContext context) : IPortalVoucherReadRepository
{
    public async Task<(int Total, IReadOnlyList<Voucher> Items)> ListPageAsync(
        PortalVoucherFilter filter, int? skip, int? take, CancellationToken cancellationToken)
    {
        var query = context.Vouchers.AsNoTracking()
            .Where(voucher => voucher.NetworkId == filter.NetworkId);

        if (filter.CampaignId is { } id)
        {
            query = query.Where(voucher => voucher.CampaignId == id);
        }

        query = query.Where(filter.Status is { } status
            ? EffectiveStatusFilters.Voucher(status, filter.UtcNow)
            : EffectiveStatusFilters.NotDiscarded(filter.UtcNow));

        if (filter.IssuedFromUtc is { } from)
        {
            query = query.Where(voucher => voucher.IssuedAt >= from);
        }

        if (filter.IssuedToUtcExclusive is { } to)
        {
            query = query.Where(voucher => voucher.IssuedAt < to);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            // Código sem diferenciar maiúsculas; CPF só pelo número inteiro (D4 da GH-050).
            var code = filter.SearchText.ToUpperInvariant();
            var digits = filter.SearchDigits is { Length: 11 } cpf ? cpf : null;
            query = query.Where(voucher => voucher.Code.ToUpper().Contains(code) || (digits != null && voucher.CustomerDocument == digits));
        }

        var total = await query.CountAsync(cancellationToken);
        var ordered = query.OrderByDescending(voucher => voucher.IssuedAt).ThenByDescending(voucher => voucher.Id);
        IQueryable<Voucher> page = ordered;

        if (skip is { } offset && take is { } size)
        {
            page = ordered.Skip(offset).Take(size);
        }

        var items = await page
            .Include(voucher => voucher.Campaign!).ThenInclude(campaign => campaign.Network)
            .Include(voucher => voucher.Item)
            .Include(voucher => voucher.Concession)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return (total, items);
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
}
