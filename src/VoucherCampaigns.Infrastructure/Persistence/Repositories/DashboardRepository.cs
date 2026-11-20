using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Dashboard;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Infrastructure.Persistence.Repositories;

/// <summary>
/// Contagens do acompanhamento (spec GH-050, D-3 e D-4): uma agregação por campanha sobre <c>vouchers</c>, com o status
/// efetivo calculado no SQL pela mesma regra de <see cref="Voucher.EffectiveStatus"/>, e outra sobre <c>redemptions</c>.
/// </summary>
public class DashboardRepository(AppDbContext context) : IDashboardRepository
{
    public async Task<IReadOnlyList<CampaignCounts>> CountVouchersAsync(
        Guid networkId, Guid? campaignId, VoucherFilter filter, DateTime utcNow, CancellationToken cancellationToken)
    {
        var now = BusinessTime.TruncateToSecond(utcNow);
        var vouchers = Filtered(networkId, campaignId, filter);

        var counts = await vouchers
            .GroupBy(voucher => voucher.CampaignId)
            .Select(group => new
            {
                CampaignId = group.Key,
                Issued = group.Count(voucher => voucher.Status != VoucherStatus.Provisional && voucher.Status != VoucherStatus.Discarded),
                Reserved = group.Count(voucher => voucher.Status == VoucherStatus.Provisional && voucher.ReservedUntil > utcNow),
                Redeemed = group.Count(voucher => voucher.Status != VoucherStatus.Provisional && voucher.Status != VoucherStatus.Discarded && voucher.UsesCount > 0),
                ExpiredUnused = group.Count(voucher => voucher.UsesCount == 0
                    && (voucher.Status == VoucherStatus.Expired || (voucher.Status == VoucherStatus.Available && voucher.ValidUntil < now))),
                CancelledUnused = group.Count(voucher => voucher.Status == VoucherStatus.Cancelled && voucher.UsesCount == 0),
                Available = group.Count(voucher => voucher.Status == VoucherStatus.Available && voucher.ValidUntil >= now && voucher.UsesCount == 0)
            })
            .ToListAsync(cancellationToken);

        var redemptions = await context.Redemptions.AsNoTracking()
            .Where(redemption => redemption.Status == RedemptionStatus.Effective)
            .Join(vouchers, redemption => redemption.VoucherId, voucher => voucher.Id, (redemption, voucher) => redemption)
            .GroupBy(redemption => redemption.CampaignId)
            .Select(group => new { CampaignId = group.Key, Count = group.Count(), Discount = group.Sum(redemption => redemption.DiscountAmount) })
            .ToDictionaryAsync(entry => entry.CampaignId, cancellationToken);

        return counts
            .Select(entry =>
            {
                var redeemed = redemptions.GetValueOrDefault(entry.CampaignId);
                return new CampaignCounts(
                    entry.CampaignId, entry.Issued, entry.Reserved, entry.Redeemed, entry.ExpiredUnused, entry.CancelledUnused, entry.Available,
                    redeemed?.Count ?? 0, redeemed?.Discount ?? 0m);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<AuditEntry>> GetCampaignAuditAsync(Guid campaignId, CancellationToken cancellationToken) =>
        await context.AuditEntries.AsNoTracking()
            .Where(entry => entry.CampaignId == campaignId && entry.EntityType == "Campaign")
            .OrderBy(entry => entry.OccurredAt)
            .ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, AuditActor>> GetUsersAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await context.Users.AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .Select(user => new AuditActor(user.Id, user.Name, user.Role))
            .ToDictionaryAsync(actor => actor.Id, cancellationToken);

    private IQueryable<Voucher> Filtered(Guid networkId, Guid? campaignId, VoucherFilter filter)
    {
        var query = context.Vouchers.AsNoTracking().Where(voucher => voucher.NetworkId == networkId);

        if (campaignId is { } onlyCampaign)
        {
            query = query.Where(voucher => voucher.CampaignId == onlyCampaign);
        }

        if (filter.FromUtc is { } from)
        {
            query = query.Where(voucher => voucher.IssuedAt >= from);
        }

        if (filter.ToUtcExclusive is { } to)
        {
            query = query.Where(voucher => voucher.IssuedAt < to);
        }

        if (filter.EstablishmentId is { } establishmentId)
        {
            query = query.Where(voucher => voucher.EstablishmentId == establishmentId);
        }

        if (filter.RegionId is { } regionId)
        {
            query = query.Where(voucher => context.Establishments.Any(store => store.Id == voucher.EstablishmentId && store.RegionId == regionId));
        }

        if (filter.CategoryId is { } categoryId)
        {
            // Categoria pelo alvo do voucher (D-6): categoria do alvo ou categoria do produto do alvo; alvo "todos" não casa.
            query = query.Where(voucher => voucher.Item!.CategoryId == categoryId || voucher.Item!.Product!.CategoryId == categoryId);
        }

        return query;
    }
}
