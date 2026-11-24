using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Customers;
using VoucherCampaigns.Domain.Customers;

namespace VoucherCampaigns.Infrastructure.Persistence.Repositories;

public class CustomerGroupRepository(AppDbContext context) : ICustomerGroupRepository
{
    public async Task<IReadOnlyList<CustomerGroup>> ListAsync(Guid networkId, bool? activeOnly, CancellationToken cancellationToken)
    {
        var query = context.CustomerGroups.AsNoTracking().Where(group => group.NetworkId == networkId);
        if (activeOnly == true)
        {
            query = query.Where(group => group.IsActive);
        }
        else if (activeOnly == false)
        {
            query = query.Where(group => !group.IsActive);
        }

        return await query.OrderBy(group => group.Name).ToListAsync(cancellationToken);
    }

    public Task<CustomerGroup?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.CustomerGroups.FirstOrDefaultAsync(group => group.Id == id, cancellationToken);

    public Task<CustomerGroup?> FindWithMembersAsync(Guid id, CancellationToken cancellationToken) =>
        context.CustomerGroups.Include(group => group.Members).FirstOrDefaultAsync(group => group.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CustomerGroup>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await context.CustomerGroups.AsNoTracking().Where(group => ids.Contains(group.Id)).ToListAsync(cancellationToken);

    public Task<bool> NameExistsAsync(Guid networkId, string name, Guid? exceptId, CancellationToken cancellationToken) =>
        context.CustomerGroups.AnyAsync(
            group => group.NetworkId == networkId
                && (exceptId == null || group.Id != exceptId)
                && group.Name.ToLower() == name.ToLower(),
            cancellationToken);

    public async Task AddAsync(CustomerGroup group, CancellationToken cancellationToken)
    {
        context.CustomerGroups.Add(group);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    public async Task<(int Total, IReadOnlyList<string> Documents)> MembersPageAsync(
        Guid groupId, int skip, int take, CancellationToken cancellationToken)
    {
        var query = context.CustomerGroupMembers.AsNoTracking().Where(member => member.GroupId == groupId);
        var total = await query.CountAsync(cancellationToken);
        var documents = await query.OrderBy(member => member.Document).Skip(skip).Take(take).Select(member => member.Document).ToListAsync(cancellationToken);
        return (total, documents);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountMembersAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken)
    {
        if (groupIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        return await context.CustomerGroupMembers.AsNoTracking()
            .Where(member => groupIds.Contains(member.GroupId))
            .GroupBy(member => member.GroupId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);
    }

    public Task<int> CountDistinctDocumentsAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken)
    {
        if (groupIds.Count == 0)
        {
            return Task.FromResult(0);
        }

        return context.CustomerGroupMembers.AsNoTracking()
            .Where(member => groupIds.Contains(member.GroupId))
            .Select(member => member.Document)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountDistinctByCampaignAsync(
        IReadOnlyCollection<Guid> campaignIds, CancellationToken cancellationToken)
    {
        if (campaignIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var rows = await (
            from link in context.CampaignCustomerGroups.AsNoTracking()
            join member in context.CustomerGroupMembers.AsNoTracking() on link.GroupId equals member.GroupId
            where campaignIds.Contains(link.CampaignId)
            select new { link.CampaignId, member.Document }).ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.CampaignId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Document).Distinct().Count());
    }
}
