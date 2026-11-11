using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Infrastructure.Persistence;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;

namespace VoucherCampaigns.IntegrationTests.Campaigns;

/// <summary>Status efetivo na leitura (PR-04), com o relógio da Api fixado em cada instante.</summary>
public class CampaignEffectiveStatusEndpointTests : IClassFixture<ApiFactory>
{
    private const string Route = ReadyCampaign.Route;

    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(-3));

    private readonly ApiFactory _factory;

    public CampaignEffectiveStatusEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task ScheduledCampaign_BecomesActiveAndEndedByTheClock_WithoutChangingTheStoredStatus()
    {
        var campaignId = await ReadyCampaign.CreateSubmittedAsync(Client(Roles.Analista));
        var approved = await Client(Roles.Gestor).PostAsync($"{Route}/{campaignId}/approve", null);
        Assert.Equal("Scheduled", (await approved.Content.ReadFromJsonAsync<CampaignResponse>())!.Status);

        _factory.Clock.UtcNow = ReadyCampaign.StartsAt.AddTicks(-1);
        Assert.Equal("Scheduled", await StatusAsync(campaignId));

        _factory.Clock.UtcNow = ReadyCampaign.StartsAt;
        Assert.Equal("Active", await StatusAsync(campaignId));

        // D9: às 23:59:59 do fim ainda vale; a partir de 00:00 está encerrada.
        _factory.Clock.UtcNow = ReadyCampaign.EndsAt.AddSeconds(59);
        Assert.Equal("Active", await StatusAsync(campaignId));

        _factory.Clock.UtcNow = ReadyCampaign.EndsAt.AddMinutes(1);
        Assert.Equal("Ended", await StatusAsync(campaignId));

        await using var scope = _factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Campaigns
            .SingleAsync(campaign => campaign.Id == campaignId);
        Assert.Equal(CampaignStatus.Scheduled, stored.Status);
    }

    [Fact]
    public async Task StatusFilter_UsesTheEffectiveStatus()
    {
        var campaignId = await ReadyCampaign.CreateSubmittedAsync(Client(Roles.Analista));
        (await Client(Roles.Gestor).PostAsync($"{Route}/{campaignId}/approve", null)).EnsureSuccessStatusCode();
        _factory.Clock.UtcNow = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.FromHours(-3));

        var actives = await ListAsync("Active");
        var scheduled = await ListAsync("Scheduled");

        var listed = Assert.Single(actives, campaign => campaign.Id == campaignId);
        Assert.Equal("Active", listed.Status);
        Assert.DoesNotContain(scheduled, campaign => campaign.Id == campaignId);
    }

    [Fact]
    public async Task DraftAndPendingApproval_KeepTheirStatusAfterTheEnd()
    {
        var draftId = await ReadyCampaign.CreateAsync(Client(Roles.Analista));
        var pendingId = await ReadyCampaign.CreateSubmittedAsync(Client(Roles.Analista));

        _factory.Clock.UtcNow = ReadyCampaign.EndsAt.AddDays(10);

        Assert.Equal("Draft", await StatusAsync(draftId));
        Assert.Equal("PendingApproval", await StatusAsync(pendingId));
    }

    private async Task<string> StatusAsync(Guid campaignId) =>
        (await Client(Roles.Analista).GetFromJsonAsync<CampaignResponse>($"{Route}/{campaignId}"))!.Status;

    private async Task<IReadOnlyList<CampaignResponse>> ListAsync(string status) =>
        (await Client(Roles.Gestor).GetFromJsonAsync<List<CampaignResponse>>(
            $"{Route}?networkId={DemoDataSeeder.NetworkId}&status={status}"))!;

    private HttpClient Client(string role) => _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, role));
}
