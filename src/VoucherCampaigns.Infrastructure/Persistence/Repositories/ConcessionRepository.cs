using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VoucherCampaigns.Application.Vouchers;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistência do motor (spec GH-045, D-4). As mudanças de saldo e de status são comandos <c>UPDATE ... WHERE</c>
/// únicos, atômicos tanto no SQLite quanto no PostgreSQL; nenhuma lê o valor para depois gravar.
/// </summary>
public class ConcessionRepository(AppDbContext context) : IConcessionRepository
{
    private static readonly CampaignStatus[] NeverApproved =
        [CampaignStatus.Draft, CampaignStatus.PendingApproval, CampaignStatus.Rejected];

    public Task<Establishment?> FindEstablishmentAsync(Guid establishmentId, Guid networkId, CancellationToken cancellationToken) =>
        context.Establishments.AsNoTracking()
            .Include(establishment => establishment.Channels)
            .FirstOrDefaultAsync(
                establishment => establishment.Id == establishmentId && establishment.Region!.NetworkId == networkId,
                cancellationToken);

    public Task<Concession?> FindByTransactionAsync(Guid networkId, string transactionId, CancellationToken cancellationToken) =>
        WithVoucher(context.Concessions.AsNoTracking())
            .FirstOrDefaultAsync(
                concession => concession.NetworkId == networkId && concession.TransactionId == transactionId, cancellationToken);

    public Task<Concession?> FindByIdAsync(Guid id, Guid networkId, CancellationToken cancellationToken) =>
        WithVoucher(context.Concessions.AsNoTracking())
            .FirstOrDefaultAsync(concession => concession.Id == id && concession.NetworkId == networkId, cancellationToken);

    public async Task<IReadOnlyList<Campaign>> GetApprovedCampaignsAsync(Guid networkId, CancellationToken cancellationToken) =>
        await context.Campaigns.AsNoTracking()
            .Where(campaign => campaign.NetworkId == networkId && !NeverApproved.Contains(campaign.Status))
            .Include(campaign => campaign.Network)
            .Include(campaign => campaign.Items.OrderBy(item => item.Id))
            .Include(campaign => campaign.Conditions.OrderBy(condition => condition.Id))
            .Include(campaign => campaign.Scopes)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountCustomerVouchersAsync(
        Guid networkId, string customerDocument, CancellationToken cancellationToken)
    {
        var counts = await context.Vouchers.AsNoTracking()
            .Where(voucher => voucher.NetworkId == networkId
                && voucher.CustomerDocument == customerDocument
                && voucher.Status != VoucherStatus.Discarded)
            .GroupBy(voucher => voucher.CampaignId)
            .Select(group => new { CampaignId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(entry => entry.CampaignId, entry => entry.Count);
    }

    public async Task<IReadOnlyList<Voucher>> GetCustomerVouchersAsync(
        Guid networkId, string customerDocument, CancellationToken cancellationToken) =>
        await context.Vouchers.AsNoTracking()
            .Where(voucher => voucher.NetworkId == networkId && voucher.CustomerDocument == customerDocument)
            .Include(voucher => voucher.Campaign!).ThenInclude(campaign => campaign.Network)
            .Include(voucher => voucher.Item)
            .Include(voucher => voucher.Concession)
            .OrderByDescending(voucher => voucher.IssuedAt)
            .ThenByDescending(voucher => voucher.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ExpiredReservation>> GetExpiredReservationsAsync(
        Guid? networkId, DateTime utcNow, CancellationToken cancellationToken) =>
        await context.Vouchers.AsNoTracking()
            .Where(voucher => voucher.Status == VoucherStatus.Provisional
                && voucher.ReservedUntil <= utcNow
                && (networkId == null || voucher.NetworkId == networkId))
            .OrderBy(voucher => voucher.ReservedUntil)
            .Select(voucher => new ExpiredReservation(voucher.Id, voucher.CampaignId, voucher.CampaignItemId, voucher.ConcessionId))
            .ToListAsync(cancellationToken);

    public async Task<IConcessionTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new ConcessionTransaction(await context.Database.BeginTransactionAsync(cancellationToken));

    public async Task<ReserveOutcome> TryReserveAsync(Guid campaignId, Guid campaignItemId, CancellationToken cancellationToken)
    {
        // Primeiro comando da transação: a linha da campanha fica travada até o commit e a condição
        // é reavaliada com o valor já gravado por quem chegou antes (RNF02).
        var campaignRows = await context.Campaigns
            .Where(campaign => campaign.Id == campaignId
                && (campaign.TotalVoucherLimit == null
                    || campaign.IssuedVouchers + campaign.ReservedVouchers < campaign.TotalVoucherLimit))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(campaign => campaign.ReservedVouchers, campaign => campaign.ReservedVouchers + 1),
                cancellationToken);

        if (campaignRows == 0)
        {
            return ReserveOutcome.CampaignSoldOut;
        }

        var itemRows = await context.CampaignItems
            .Where(item => item.Id == campaignItemId
                && (item.VoucherLimit == null || item.IssuedVouchers + item.ReservedVouchers < item.VoucherLimit))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.ReservedVouchers, item => item.ReservedVouchers + 1),
                cancellationToken);

        return itemRows == 0 ? ReserveOutcome.TargetSoldOut : ReserveOutcome.Reserved;
    }

    public async Task<bool> TryConfirmVoucherAsync(Guid voucherId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var rows = await context.Vouchers
            .Where(voucher => voucher.Id == voucherId
                && voucher.Status == VoucherStatus.Provisional
                && voucher.ReservedUntil > utcNow)
            .ExecuteUpdateAsync(
                setters => setters
                    // Resgatado na própria compra até o limite (GH-049, D-10) já nasce resgatado ao confirmar.
                    .SetProperty(
                        voucher => voucher.Status,
                        voucher => voucher.UsesCount >= voucher.MaxUses ? VoucherStatus.Redeemed : VoucherStatus.Available)
                    .SetProperty(voucher => voucher.ConfirmedAt, utcNow)
                    .SetProperty(voucher => voucher.ReservedUntil, (DateTime?)null),
                cancellationToken);

        return rows == 1;
    }

    public async Task<bool> TryDiscardVoucherAsync(Guid voucherId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var rows = await context.Vouchers
            .Where(voucher => voucher.Id == voucherId && voucher.Status == VoucherStatus.Provisional)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(voucher => voucher.Status, VoucherStatus.Discarded)
                    .SetProperty(voucher => voucher.DiscardedAt, utcNow)
                    .SetProperty(voucher => voucher.ReservedUntil, (DateTime?)null),
                cancellationToken);

        return rows == 1;
    }

    public async Task ConsumeReservationAsync(Guid campaignId, Guid campaignItemId, CancellationToken cancellationToken)
    {
        await context.Campaigns
            .Where(campaign => campaign.Id == campaignId && campaign.ReservedVouchers > 0)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(campaign => campaign.ReservedVouchers, campaign => campaign.ReservedVouchers - 1)
                    .SetProperty(campaign => campaign.IssuedVouchers, campaign => campaign.IssuedVouchers + 1),
                cancellationToken);

        await context.CampaignItems
            .Where(item => item.Id == campaignItemId && item.ReservedVouchers > 0)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.ReservedVouchers, item => item.ReservedVouchers - 1)
                    .SetProperty(item => item.IssuedVouchers, item => item.IssuedVouchers + 1),
                cancellationToken);
    }

    public async Task ReleaseReservationAsync(Guid campaignId, Guid campaignItemId, CancellationToken cancellationToken)
    {
        await context.Campaigns
            .Where(campaign => campaign.Id == campaignId && campaign.ReservedVouchers > 0)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(campaign => campaign.ReservedVouchers, campaign => campaign.ReservedVouchers - 1),
                cancellationToken);

        await context.CampaignItems
            .Where(item => item.Id == campaignItemId && item.ReservedVouchers > 0)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.ReservedVouchers, item => item.ReservedVouchers - 1),
                cancellationToken);
    }

    public Task MarkConcessionAsync(Guid concessionId, ConcessionStatus status, DateTime decidedAt, CancellationToken cancellationToken) =>
        context.Concessions
            .Where(concession => concession.Id == concessionId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(concession => concession.Status, status)
                    .SetProperty(concession => concession.DecidedAt, decidedAt)
                    .SetProperty(concession => concession.ReservedUntil, (DateTime?)null),
                cancellationToken);

    public void Add(Concession concession) => context.Concessions.Add(concession);

    public void Add(Voucher voucher) => context.Vouchers.Add(voucher);

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
            // As entidades ficam marcadas como novas no contexto; quem chamou não grava de novo nesta requisição.
            return false;
        }
    }

    private static IQueryable<Concession> WithVoucher(IQueryable<Concession> query) =>
        query
            .Include(concession => concession.Voucher!).ThenInclude(voucher => voucher.Campaign!).ThenInclude(campaign => campaign.Network)
            .Include(concession => concession.Voucher!).ThenInclude(voucher => voucher.Item);

    private sealed class ConcessionTransaction(IDbContextTransaction transaction) : IConcessionTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
