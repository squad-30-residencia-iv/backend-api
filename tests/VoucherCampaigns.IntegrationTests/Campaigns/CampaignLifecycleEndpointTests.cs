using System.Net;
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

public class CampaignLifecycleEndpointTests : IClassFixture<ApiFactory>
{
    private const string Route = "/api/v1/campaigns";

    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(-3));

    private readonly ApiFactory _factory;

    public CampaignLifecycleEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task Submit_ByAnalista_StoresPendingAndTheEvent()
    {
        var campaignId = await CreateReadyCampaignAsync();

        var response = await Client().PostAsync($"{Route}/{campaignId}/submit", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CampaignResponse>();
        Assert.Equal("PendingApproval", body!.Status);
        Assert.Null(body.RejectionReason);

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await context.Campaigns.SingleAsync(campaign => campaign.Id == campaignId);
        var statusEvent = await context.CampaignStatusEvents.SingleAsync(item => item.CampaignId == campaignId);

        Assert.Equal(CampaignStatus.PendingApproval, stored.Status);
        Assert.NotNull(stored.SubmittedByUserId);
        Assert.Equal(stored.SubmittedByUserId, statusEvent.ActorUserId);
        Assert.Equal(CampaignStatus.Draft, statusEvent.FromStatus);
        Assert.Equal(CampaignStatus.PendingApproval, statusEvent.ToStatus);
        Assert.Null(statusEvent.Reason);
    }

    [Fact]
    public async Task Submit_ByGestor_ReturnsForbidden()
    {
        var campaignId = await CreateReadyCampaignAsync();

        var response = await Client(Roles.Gestor).PostAsync($"{Route}/{campaignId}/submit", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Submit_WithoutAudience_Returns422()
    {
        var created = await Client().PostAsJsonAsync(Route, new
        {
            networkId = DemoDataSeeder.NetworkId,
            name = "Rascunho incompleto",
            startsAt = "2026-10-01T08:00:00",
            endsAt = "2026-10-31T23:59:00",
        });
        var campaign = await created.Content.ReadFromJsonAsync<CampaignResponse>();

        var response = await Client().PostAsync($"{Route}/{campaign!.Id}/submit", null);

        await AssertBusinessRuleAsync(response, CampaignErrors.SubmitAudienceRequiredCode, CampaignErrors.SubmitAudienceRequiredMessage);
    }

    [Fact]
    public async Task Submit_Twice_Returns422()
    {
        var campaignId = await CreateReadyCampaignAsync();
        await Client().PostAsync($"{Route}/{campaignId}/submit", null);

        var response = await Client().PostAsync($"{Route}/{campaignId}/submit", null);

        await AssertBusinessRuleAsync(response, CampaignErrors.SubmitNotDraftCode, CampaignErrors.SubmitNotDraftMessage);
    }

    [Fact]
    public async Task Approve_BeforeStart_StoresScheduled()
    {
        var campaignId = await SubmitReadyCampaignAsync();

        var response = await Client(Roles.Gestor).PostAsync($"{Route}/{campaignId}/approve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CampaignResponse>();
        Assert.Equal("Scheduled", body!.Status);

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await context.Campaigns.SingleAsync(campaign => campaign.Id == campaignId);
        Assert.Equal(CampaignStatus.Scheduled, stored.Status);
        Assert.Equal(2, await context.CampaignStatusEvents.CountAsync(item => item.CampaignId == campaignId));
    }

    [Fact]
    public async Task Approve_DuringPeriod_StoresActive()
    {
        var campaignId = await SubmitReadyCampaignAsync();
        _factory.Clock.UtcNow = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.FromHours(-3));

        var response = await Client(Roles.Gestor).PostAsync($"{Route}/{campaignId}/approve", null);

        var body = await response.Content.ReadFromJsonAsync<CampaignResponse>();
        Assert.Equal("Active", body!.Status);
    }

    [Fact]
    public async Task Approve_AfterEnd_Returns422()
    {
        var campaignId = await SubmitReadyCampaignAsync();
        _factory.Clock.UtcNow = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.FromHours(-3));

        var response = await Client(Roles.Gestor).PostAsync($"{Route}/{campaignId}/approve", null);

        await AssertBusinessRuleAsync(response, CampaignErrors.ApprovePeriodEndedCode, CampaignErrors.ApprovePeriodEndedMessage);
    }

    [Fact]
    public async Task Approve_ByAnalista_ReturnsForbidden()
    {
        var campaignId = await SubmitReadyCampaignAsync();

        var response = await Client().PostAsync($"{Route}/{campaignId}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reject_StoresReasonOnCampaignAndEvent()
    {
        var campaignId = await SubmitReadyCampaignAsync();

        var response = await Client(Roles.Gestor).PostAsJsonAsync($"{Route}/{campaignId}/reject", new { reason = " Fora do calendário " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CampaignResponse>();
        Assert.Equal("Rejected", body!.Status);
        Assert.Equal("Fora do calendário", body.RejectionReason);

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var statusEvent = await context.CampaignStatusEvents.SingleAsync(item => item.CampaignId == campaignId && item.ToStatus == CampaignStatus.Rejected);
        Assert.Equal("Fora do calendário", statusEvent.Reason);
    }

    [Fact]
    public async Task Submit_FromAnotherNetwork_ReturnsForbidden()
    {
        var campaignId = await CreateReadyCampaignAsync();

        var response = await Client(Roles.Analista, TestAuth.OtherNetworkId).PostAsync($"{Route}/{campaignId}/submit", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> SubmitReadyCampaignAsync()
    {
        var campaignId = await CreateReadyCampaignAsync();
        var response = await Client().PostAsync($"{Route}/{campaignId}/submit", null);
        response.EnsureSuccessStatusCode();
        return campaignId;
    }

    private async Task<Guid> CreateReadyCampaignAsync()
    {
        var created = await Client().PostAsJsonAsync(Route, new
        {
            networkId = DemoDataSeeder.NetworkId,
            name = $"Campanha {Guid.NewGuid():N}",
            startsAt = "2026-10-01T08:00:00",
            endsAt = "2026-10-31T23:59:00",
        });
        created.EnsureSuccessStatusCode();
        var campaign = await created.Content.ReadFromJsonAsync<CampaignResponse>();
        var id = campaign!.Id;
        (await Client().PutAsJsonAsync($"{Route}/{id}/items", new { items = new[] { new { targetType = "All", benefitType = "Percentage", benefitValue = 10 } } })).EnsureSuccessStatusCode();
        (await Client().PutAsJsonAsync($"{Route}/{id}/quantities", new { totalVoucherLimit = 100, perCustomerVoucherLimit = 2 })).EnsureSuccessStatusCode();
        (await Client().PutAsJsonAsync($"{Route}/{id}/audience", new { audienceType = "AllCustomers" })).EnsureSuccessStatusCode();
        (await Client().PutAsJsonAsync($"{Route}/{id}/voucher-validity", new { type = "Relative", amount = 30, unit = "Days" })).EnsureSuccessStatusCode();
        (await Client().PutAsJsonAsync($"{Route}/{id}/usage-restrictions", new { maxUsesPerVoucher = 1 })).EnsureSuccessStatusCode();
        return id;
    }

    private HttpClient Client(string role = Roles.Analista, Guid? networkId = null) =>
        _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, role, networkId));

    private static async Task AssertBusinessRuleAsync(HttpResponseMessage response, string code, string message)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal(message, problem.GetProperty("detail").GetString());
    }
}
