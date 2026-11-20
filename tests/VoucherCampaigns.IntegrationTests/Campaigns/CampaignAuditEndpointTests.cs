using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Infrastructure.Persistence;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;

namespace VoucherCampaigns.IntegrationTests.Campaigns;

public class CampaignAuditEndpointTests : IClassFixture<ApiFactory>
{
    private const string Route = ReadyCampaign.Route;

    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(-3));
    private static readonly Guid AnalistaId = Guid.Parse("0199e3a0-0000-7000-8000-000000000a01");
    private static readonly Guid GestorId = Guid.Parse("0199e3a0-0000-7000-8000-000000000a02");

    private readonly ApiFactory _factory;

    public CampaignAuditEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task Submit_AppendsEntryWithActorScopeInstantAndPayload()
    {
        var campaignId = await ReadyCampaign.CreateAsync(Analista());

        (await Analista().PostAsync($"{Route}/{campaignId}/submit", null)).EnsureSuccessStatusCode();

        var entry = Assert.Single(await EntriesAsync(campaignId));
        Assert.Equal(AuditEventTypes.CampaignSubmitted, entry.EventType);
        Assert.Equal(AuditActorType.User, entry.ActorType);
        Assert.Equal(AnalistaId, entry.ActorId);
        Assert.Equal(DemoDataSeeder.NetworkId, entry.NetworkId);
        Assert.Equal("Campaign", entry.EntityType);
        Assert.Equal(campaignId, entry.EntityId);
        Assert.Equal(Now.UtcDateTime, entry.OccurredAt);
        Assert.Equal("""{"fromStatus":"Draft","toStatus":"PendingApproval"}""", entry.Payload);
    }

    [Fact]
    public async Task Approve_AppendsEntryAndKeepsTheSubmissionUnchanged()
    {
        var campaignId = await ReadyCampaign.CreateSubmittedAsync(Analista());
        var submitted = Assert.Single(await EntriesAsync(campaignId));
        _factory.Clock.UtcNow = Now.AddHours(1);

        (await Gestor().PostAsync($"{Route}/{campaignId}/approve", null)).EnsureSuccessStatusCode();

        var entries = await EntriesAsync(campaignId);
        Assert.Equal(2, entries.Count);
        Assert.Equivalent(submitted, entries[0]);

        var approved = entries[1];
        Assert.Equal(AuditEventTypes.CampaignApproved, approved.EventType);
        Assert.Equal(GestorId, approved.ActorId);
        Assert.Equal(Now.AddHours(1).UtcDateTime, approved.OccurredAt);
        Assert.Equal("""{"fromStatus":"PendingApproval","toStatus":"Scheduled"}""", approved.Payload);
    }

    [Fact]
    public async Task Reject_AppendsEntryWithTheReason()
    {
        var campaignId = await ReadyCampaign.CreateSubmittedAsync(Analista());

        (await Gestor().PostAsJsonAsync($"{Route}/{campaignId}/reject", new { reason = "Fora do calendário" })).EnsureSuccessStatusCode();

        var rejected = Assert.Single(await EntriesAsync(campaignId), entry => entry.EventType == AuditEventTypes.CampaignRejected);
        Assert.Equal(GestorId, rejected.ActorId);
        Assert.Equal("""{"fromStatus":"PendingApproval","toStatus":"Rejected","reason":"Fora do calendário"}""", rejected.Payload);
    }

    [Fact]
    public async Task RefusedOperations_DoNotAppendEntries()
    {
        var created = await Analista().PostAsJsonAsync(Route, new
        {
            networkId = DemoDataSeeder.NetworkId,
            name = "Rascunho incompleto",
            startsAt = "2026-10-01T08:00:00",
            endsAt = "2026-10-31T23:59:00",
        });
        var incompleteId = (await created.Content.ReadFromJsonAsync<CampaignResponse>())!.Id;
        var submittedId = await ReadyCampaign.CreateSubmittedAsync(Analista());
        _factory.Clock.UtcNow = ReadyCampaign.EndsAt.AddDays(1);

        var refusedSubmit = await Analista().PostAsync($"{Route}/{incompleteId}/submit", null);
        var refusedApprove = await Gestor().PostAsync($"{Route}/{submittedId}/approve", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refusedSubmit.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refusedApprove.StatusCode);
        Assert.Empty(await EntriesAsync(incompleteId));
        Assert.Single(await EntriesAsync(submittedId));
    }

    [Fact]
    public async Task AuditAndStatusHistory_CannotBeChangedOrDeleted()
    {
        var campaignId = await ReadyCampaign.CreateSubmittedAsync(Analista());
        var original = Assert.Single(await EntriesAsync(campaignId));

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entry = await context.AuditEntries.SingleAsync(item => item.CampaignId == campaignId && item.EventType == AuditEventTypes.CampaignSubmitted);
            entry.Payload = "{}";

            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.AuditEntries.Remove(await context.AuditEntries.SingleAsync(item => item.CampaignId == campaignId && item.EventType == AuditEventTypes.CampaignSubmitted));

            Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var statusEvent = await context.CampaignStatusEvents.SingleAsync(item => item.CampaignId == campaignId);
            statusEvent.Reason = "alterado";

            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        Assert.Equivalent(original, Assert.Single(await EntriesAsync(campaignId)));

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null((await context.CampaignStatusEvents.SingleAsync(item => item.CampaignId == campaignId)).Reason);
        }
    }

    private async Task<List<AuditEntry>> EntriesAsync(Guid campaignId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Só o ciclo de vida: a criação e as edições do rascunho (GH-050, D-8) têm testes próprios em Dashboard/CampaignTrailEndpointTests.
        return await context.AuditEntries.AsNoTracking()
            .Where(entry => entry.CampaignId == campaignId
                && entry.EventType != AuditEventTypes.CampaignCreated
                && entry.EventType != AuditEventTypes.CampaignUpdated)
            .OrderBy(entry => entry.Id)
            .ToListAsync();
    }

    private HttpClient Analista() =>
        _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, Roles.Analista, userId: AnalistaId));

    private HttpClient Gestor() =>
        _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, Roles.Gestor, userId: GestorId));
}
