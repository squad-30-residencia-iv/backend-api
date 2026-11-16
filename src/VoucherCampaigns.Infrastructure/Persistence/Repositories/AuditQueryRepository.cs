using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Audit;

namespace VoucherCampaigns.Infrastructure.Persistence.Repositories;

public class AuditQueryRepository(AppDbContext context) : IAuditQueryRepository
{
    public async Task<IReadOnlyList<AuditEntryListRow>> ListAsync(
        Guid networkId,
        string? eventType,
        Guid? campaignId,
        DateTime? fromUtc,
        DateTime? toUtcExclusive,
        CancellationToken cancellationToken)
    {
        var network = await context.Networks.AsNoTracking()
            .FirstAsync(item => item.Id == networkId, cancellationToken);

        var query = context.AuditEntries.AsNoTracking()
            .Where(entry => entry.NetworkId == networkId);

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(entry => entry.EventType == eventType);
        }

        if (campaignId is { } id)
        {
            query = query.Where(entry => entry.CampaignId == id);
        }

        if (fromUtc is { } from)
        {
            query = query.Where(entry => entry.OccurredAt >= from);
        }

        if (toUtcExclusive is { } to)
        {
            query = query.Where(entry => entry.OccurredAt < to);
        }

        var entries = await query
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .ToListAsync(cancellationToken);

        var campaignIds = entries
            .Where(entry => entry.CampaignId is not null)
            .Select(entry => entry.CampaignId!.Value)
            .Distinct()
            .ToList();

        var names = campaignIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await context.Campaigns.AsNoTracking()
                .Where(campaign => campaignIds.Contains(campaign.Id))
                .ToDictionaryAsync(campaign => campaign.Id, campaign => campaign.Name, cancellationToken);

        return entries.Select(entry => new AuditEntryListRow(
            entry.Id,
            entry.EventType,
            entry.ActorType.ToString(),
            entry.ActorId,
            entry.NetworkId,
            entry.CampaignId,
            entry.CampaignId is { } campaignKey && names.TryGetValue(campaignKey, out var name) ? name : null,
            entry.EntityType,
            entry.EntityId,
            entry.OccurredAt,
            entry.Payload,
            network.TimeZone)).ToList();
    }

    public async Task<AuditEntryListRow?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await context.AuditEntries.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (entry is null)
        {
            return null;
        }

        string? timeZone = null;
        if (entry.NetworkId is { } networkId)
        {
            timeZone = await context.Networks.AsNoTracking()
                .Where(network => network.Id == networkId)
                .Select(network => network.TimeZone)
                .FirstOrDefaultAsync(cancellationToken);
        }

        string? campaignName = null;
        if (entry.CampaignId is { } campaignId)
        {
            campaignName = await context.Campaigns.AsNoTracking()
                .Where(campaign => campaign.Id == campaignId)
                .Select(campaign => campaign.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new AuditEntryListRow(
            entry.Id,
            entry.EventType,
            entry.ActorType.ToString(),
            entry.ActorId,
            entry.NetworkId,
            entry.CampaignId,
            campaignName,
            entry.EntityType,
            entry.EntityId,
            entry.OccurredAt,
            entry.Payload,
            timeZone);
    }
}
