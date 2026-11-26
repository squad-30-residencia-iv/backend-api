using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Infrastructure.Persistence.Repositories;

/// <summary>Tags de campanha da rede (spec GH-077).</summary>
public class CampaignTagRepository(AppDbContext context) : ICampaignTagRepository
{
    public async Task<IReadOnlyList<CampaignTag>> ListAsync(Guid networkId, bool? activeOnly, CancellationToken cancellationToken)
    {
        var query = context.CampaignTags.AsNoTracking().Where(tag => tag.NetworkId == networkId);

        if (activeOnly is { } active)
        {
            query = query.Where(tag => tag.IsActive == active);
        }

        return await query.OrderBy(tag => tag.NormalizedName).ToListAsync(cancellationToken);
    }

    public Task<CampaignTag?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        context.CampaignTags.FirstOrDefaultAsync(tag => tag.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CampaignTag>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        ids.Count == 0
            ? []
            : await context.CampaignTags.AsNoTracking().Where(tag => ids.Contains(tag.Id)).ToListAsync(cancellationToken);

    public Task<bool> NameExistsAsync(Guid networkId, string normalizedName, Guid? exceptId, CancellationToken cancellationToken) =>
        context.CampaignTags.AnyAsync(
            tag => tag.NetworkId == networkId && tag.NormalizedName == normalizedName && (exceptId == null || tag.Id != exceptId),
            cancellationToken);

    public async Task AddAsync(CampaignTag tag, CancellationToken cancellationToken)
    {
        context.CampaignTags.Add(tag);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountCampaignsAsync(IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken)
    {
        if (tagIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        return await context.CampaignTagLinks.AsNoTracking()
            .Where(link => tagIds.Contains(link.TagId))
            .GroupBy(link => link.TagId)
            .Select(group => new { TagId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.TagId, entry => entry.Count, cancellationToken);
    }
}
