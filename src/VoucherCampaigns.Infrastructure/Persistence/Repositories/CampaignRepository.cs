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

    public Task LoadEligibleCustomersAsync(Campaign campaign, CancellationToken cancellationToken) =>
        context.Entry(campaign).Collection(item => item.EligibleCustomers).LoadAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> GetEligibleDocumentsAsync(Guid campaignId, CancellationToken cancellationToken) =>
        await context.CampaignEligibleCustomers.AsNoTracking()
            .Where(customer => customer.CampaignId == campaignId)
            .OrderBy(customer => customer.Document)
            .Select(customer => customer.Document)
            .ToListAsync(cancellationToken);

    public async Task<(int Total, IReadOnlyList<string> Documents)> GetEligibleDocumentsPageAsync(
        Guid campaignId, int skip, int take, CancellationToken cancellationToken)
    {
        var query = context.CampaignEligibleCustomers.AsNoTracking().Where(customer => customer.CampaignId == campaignId);
        var total = await query.CountAsync(cancellationToken);
        var documents = await query
            .OrderBy(customer => customer.Document)
            .Skip(skip)
            .Take(take)
            .Select(customer => customer.Document)
            .ToListAsync(cancellationToken);

        return (total, documents);
    }

    public Task<bool> IsCustomerListedAsync(Guid campaignId, string document, CancellationToken cancellationToken) =>
        context.CampaignEligibleCustomers.AnyAsync(
            customer => customer.CampaignId == campaignId && customer.Document == document, cancellationToken);

    public async Task<bool> SaveIfUnchangedAsync(
        Campaign campaign, CancellationToken cancellationToken, Func<CancellationToken, Task>? withinTransaction = null)
    {
        var loadedUpdatedAt = DateTime.SpecifyKind(
            context.Entry(campaign).Property(item => item.UpdatedAt).OriginalValue, DateTimeKind.Utc);

        // Duas requisições no mesmo instante (ou o relógio parado dos testes) teriam a mesma marca: a segunda passaria.
        if (campaign.UpdatedAt <= loadedUpdatedAt)
        {
            campaign.UpdatedAt = loadedUpdatedAt.AddMilliseconds(1);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // A marca é o primeiro comando: no PostgreSQL a segunda requisição espera o bloqueio da linha e, depois do
        // commit da primeira, não encontra mais o updated_at que leu; no SQLite o escritor é único.
        var claimed = await context.Campaigns
            .Where(item => item.Id == campaign.Id && item.UpdatedAt == loadedUpdatedAt)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.UpdatedAt, campaign.UpdatedAt), cancellationToken);

        if (claimed == 0)
        {
            return false;
        }

        if (withinTransaction is not null)
        {
            await withinTransaction(cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public void AddStatusEvent(CampaignStatusEvent statusEvent) => context.CampaignStatusEvents.Add(statusEvent);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    // Itens e condições na ordem em que foram informados: os ids (Guid v7) são gerados em sequência.
    // O escopo vem com região e loja para a resposta trazer os nomes (US-02.3).
    // Consulta dividida: duas coleções na mesma consulta multiplicariam as linhas.
    private static IQueryable<Campaign> WithItems(IQueryable<Campaign> query) =>
        query.Include(campaign => campaign.Items.OrderBy(item => item.Id))
            .Include(campaign => campaign.Conditions.OrderBy(condition => condition.Id))
            .Include(campaign => campaign.EligibilityCriteria.OrderBy(criterion => criterion.Id))
            .Include(campaign => campaign.Scopes).ThenInclude(scope => scope.Region)
            .Include(campaign => campaign.Scopes).ThenInclude(scope => scope.Establishment)
            .AsSplitQuery();
}
