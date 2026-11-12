using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VoucherCampaigns.Application.Vouchers;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistência do resgate (spec GH-049, D-9 e D-19). Utilização consumida e devolvida por <c>UPDATE ... WHERE</c>
/// únicos, atômicos no SQLite e no PostgreSQL; nenhuma operação lê o contador para depois gravar.
/// </summary>
public class RedemptionRepository(AppDbContext context) : IRedemptionRepository
{
    public async Task<IReadOnlyList<Voucher>> GetCustomerVouchersAsync(
        Guid networkId, string customerDocument, CancellationToken cancellationToken) =>
        await WithNavigations(context.Vouchers.AsNoTracking())
            .Where(voucher => voucher.NetworkId == networkId
                && voucher.CustomerDocument == customerDocument
                && voucher.Status != VoucherStatus.Discarded)
            .OrderByDescending(voucher => voucher.IssuedAt)
            .ThenByDescending(voucher => voucher.Id)
            .ToListAsync(cancellationToken);

    public Task<Voucher?> FindVoucherAsync(Guid voucherId, Guid networkId, string customerDocument, CancellationToken cancellationToken) =>
        WithNavigations(context.Vouchers.AsNoTracking())
            .FirstOrDefaultAsync(
                voucher => voucher.Id == voucherId && voucher.NetworkId == networkId && voucher.CustomerDocument == customerDocument,
                cancellationToken);

    public Task<Redemption?> FindByTransactionAsync(Guid networkId, string transactionId, CancellationToken cancellationToken) =>
        WithVoucher(context.Redemptions.AsNoTracking())
            .FirstOrDefaultAsync(
                redemption => redemption.NetworkId == networkId && redemption.TransactionId == transactionId, cancellationToken);

    public Task<Redemption?> FindByIdAsync(Guid id, Guid networkId, CancellationToken cancellationToken) =>
        WithVoucher(context.Redemptions.AsNoTracking())
            .FirstOrDefaultAsync(redemption => redemption.Id == id && redemption.NetworkId == networkId, cancellationToken);

    public Task<Redemption?> FindAsync(Guid redemptionId, CancellationToken cancellationToken) =>
        context.Redemptions.AsNoTracking().FirstOrDefaultAsync(redemption => redemption.Id == redemptionId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetEffectiveIdsByVoucherAsync(Guid voucherId, CancellationToken cancellationToken) =>
        await context.Redemptions.AsNoTracking()
            .Where(redemption => redemption.VoucherId == voucherId && redemption.Status == RedemptionStatus.Effective)
            .OrderBy(redemption => redemption.Id)
            .Select(redemption => redemption.Id)
            .ToListAsync(cancellationToken);

    public async Task<IConcessionTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new EfTransaction(await context.Database.BeginTransactionAsync(cancellationToken));

    public async Task<bool> TryConsumeUseAsync(Guid voucherId, DateTime utcNow, CancellationToken cancellationToken)
    {
        // Primeiro comando da transação: a linha fica travada até o commit e a condição é reavaliada com o valor gravado.
        var rows = await context.Vouchers
            .Where(voucher => voucher.Id == voucherId
                && voucher.UsesCount < voucher.MaxUses
                && (voucher.Status == VoucherStatus.Available
                    || (voucher.Status == VoucherStatus.Provisional && voucher.ReservedUntil > utcNow)))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(voucher => voucher.UsesCount, voucher => voucher.UsesCount + 1)
                    .SetProperty(
                        voucher => voucher.Status,
                        voucher => voucher.Status == VoucherStatus.Available && voucher.UsesCount + 1 >= voucher.MaxUses
                            ? VoucherStatus.Redeemed
                            : voucher.Status),
                cancellationToken);

        return rows == 1;
    }

    public async Task<bool> TryReturnUseAsync(Guid voucherId, CancellationToken cancellationToken)
    {
        var rows = await context.Vouchers
            .Where(voucher => voucher.Id == voucherId && voucher.UsesCount > 0)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(voucher => voucher.UsesCount, voucher => voucher.UsesCount - 1)
                    .SetProperty(
                        voucher => voucher.Status,
                        voucher => voucher.Status == VoucherStatus.Redeemed ? VoucherStatus.Available : voucher.Status),
                cancellationToken);

        return rows == 1;
    }

    public async Task<bool> TryReverseAsync(Guid redemptionId, DateTime reversedAt, CancellationToken cancellationToken)
    {
        var rows = await context.Redemptions
            .Where(redemption => redemption.Id == redemptionId && redemption.Status == RedemptionStatus.Effective)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(redemption => redemption.Status, RedemptionStatus.Reversed)
                    .SetProperty(redemption => redemption.ReversedAt, reversedAt),
                cancellationToken);

        return rows == 1;
    }

    public async Task<bool> TryMarkExpiredAsync(Guid voucherId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var rows = await context.Vouchers
            .Where(voucher => voucher.Id == voucherId && voucher.Status == VoucherStatus.Available && voucher.ValidUntil < utcNow)
            .ExecuteUpdateAsync(setters => setters.SetProperty(voucher => voucher.Status, VoucherStatus.Expired), cancellationToken);

        return rows == 1;
    }

    public void Add(Redemption redemption) => context.Redemptions.Add(redemption);

    public void Add(VoucherEvent voucherEvent) => context.VoucherEvents.Add(voucherEvent);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (ConstraintSave.IsUniqueViolation(exception))
        {
            return false;
        }
    }

    private static IQueryable<Voucher> WithNavigations(IQueryable<Voucher> query) =>
        query
            .Include(voucher => voucher.Campaign!).ThenInclude(campaign => campaign.Network)
            .Include(voucher => voucher.Campaign!).ThenInclude(campaign => campaign.Scopes)
            .Include(voucher => voucher.Item)
            .Include(voucher => voucher.Concession)
            .AsSplitQuery();

    private static IQueryable<Redemption> WithVoucher(IQueryable<Redemption> query) =>
        query
            .Include(redemption => redemption.Voucher!).ThenInclude(voucher => voucher.Campaign!).ThenInclude(campaign => campaign.Network)
            .Include(redemption => redemption.Voucher!).ThenInclude(voucher => voucher.Item)
            .Include(redemption => redemption.Voucher!).ThenInclude(voucher => voucher.Concession)
            .AsSplitQuery();

    private sealed class EfTransaction(IDbContextTransaction transaction) : IConcessionTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
