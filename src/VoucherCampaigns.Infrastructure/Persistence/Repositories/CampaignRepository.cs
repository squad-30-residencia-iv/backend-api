using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Infrastructure.Persistence.Repositories;

public class CampaignRepository(AppDbContext context) : ICampaignRepository
{
    public async Task AddAsync(Campaign campaign, CancellationToken cancellationToken)
    {
        context.Campaigns.Add(campaign);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Campaign>> GetByNetworkAsync(Guid networkId, CancellationToken cancellationToken)
    {
        // O Id (Guid v7) desempata campanhas criadas no mesmo instante.
        return await WithItems(context.Campaigns.AsNoTracking())
            .Where(campaign => campaign.NetworkId == networkId)
            .OrderByDescending(campaign => campaign.CreatedAt)
            .ThenByDescending(campaign => campaign.Id)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    public Task<Campaign?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        WithItems(context.Campaigns.AsNoTracking())
            .Include(campaign => campaign.Network)
            .FirstOrDefaultAsync(campaign => campaign.Id == id, cancellationToken);

    public Task<Campaign?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        WithItems(context.Campaigns)
            .Include(campaign => campaign.Network)
            .FirstOrDefaultAsync(campaign => campaign.Id == id, cancellationToken);

    public void AddStatusEvent(CampaignStatusEvent statusEvent) => context.CampaignStatusEvents.Add(statusEvent);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    // Itens e condições na ordem em que foram informados: os ids (Guid v7) são gerados em sequência.
    // Consulta dividida: duas coleções na mesma consulta multiplicariam as linhas.
    private static IQueryable<Campaign> WithItems(IQueryable<Campaign> query) =>
        query.Include(campaign => campaign.Items.OrderBy(item => item.Id))
            .Include(campaign => campaign.Conditions.OrderBy(condition => condition.Id))
            .AsSplitQuery();
}
