using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Infrastructure.Persistence;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Campaigns;

namespace VoucherCampaigns.IntegrationTests.Audit;

public class AppendOnlyTableTriggersTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task AuditEntries_DirectUpdateOrDelete_IsBlockedByTrigger()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entry = new AuditEntry
        {
            Id = Guid.CreateVersion7(),
            EventType = AuditEventTypes.CampaignSubmitted,
            ActorType = AuditActorType.User,
            ActorId = Guid.NewGuid(),
            NetworkId = DemoDataSeeder.NetworkId,
            CampaignId = Guid.NewGuid(),
            EntityType = "Campaign",
            EntityId = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow,
            Payload = "{}"
        };

        db.AuditEntries.Add(entry);
        await db.SaveChangesAsync();

        // Tentativa de UPDATE direto via ExecuteUpdateAsync deve falhar na trigger do banco
        await Assert.ThrowsAnyAsync<Exception>(() =>
            db.AuditEntries.Where(e => e.Id == entry.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.EntityType, "ModifiedEntity")));

        // Tentativa de DELETE direto via ExecuteDeleteAsync deve falhar na trigger do banco
        await Assert.ThrowsAnyAsync<Exception>(() =>
            db.AuditEntries.Where(e => e.Id == entry.Id)
                .ExecuteDeleteAsync());
    }

    [Fact]
    public async Task CampaignStatusEvents_DirectUpdateOrDelete_IsBlockedByTrigger()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var campaign = await db.Campaigns.FirstOrDefaultAsync();
        if (campaign is null)
        {
            campaign = new Campaign
            {
                Id = Guid.CreateVersion7(),
                Name = "Campanha Triggers Test",
                NetworkId = DemoDataSeeder.NetworkId,
                Status = CampaignStatus.Draft,
                StartsAt = DateTime.UtcNow.AddDays(1),
                EndsAt = DateTime.UtcNow.AddDays(10),
                CreatedAt = DateTime.UtcNow
            };
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync();
        }

        var statusEvent = new CampaignStatusEvent
        {
            Id = Guid.CreateVersion7(),
            CampaignId = campaign.Id,
            FromStatus = CampaignStatus.Draft,
            ToStatus = CampaignStatus.PendingApproval,
            ActorUserId = Guid.NewGuid(),
            Reason = "Submetendo",
            CreatedAt = DateTime.UtcNow
        };

        db.CampaignStatusEvents.Add(statusEvent);
        await db.SaveChangesAsync();

        // Tentativa de UPDATE direto via ExecuteUpdateAsync deve falhar na trigger do banco
        await Assert.ThrowsAnyAsync<Exception>(() =>
            db.CampaignStatusEvents.Where(e => e.Id == statusEvent.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Reason, "ModifiedReason")));

        // Tentativa de DELETE direto via ExecuteDeleteAsync deve falhar na trigger do banco
        await Assert.ThrowsAnyAsync<Exception>(() =>
            db.CampaignStatusEvents.Where(e => e.Id == statusEvent.Id)
                .ExecuteDeleteAsync());
    }
}
