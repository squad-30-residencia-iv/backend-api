using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Infrastructure.Persistence;
using VoucherCampaigns.IntegrationTests.Authentication;

namespace VoucherCampaigns.IntegrationTests.Campaigns;

/// <summary>Duplicar (US-01.10), pausar e encerrar (US-01.8), com o relógio da Api fixado.</summary>
public class CampaignCycleEndpointTests : IClassFixture<ApiFactory>
{
    private const string Route = ReadyCampaign.Route;

    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset DuringPeriod = new(2026, 10, 15, 12, 0, 0, TimeSpan.FromHours(-3));

    private readonly ApiFactory _factory;

    public CampaignCycleEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task Duplicate_ApprovedCampaign_CreatesDraftWithTheWholeConfiguration()
    {
        var sourceId = await ApprovedCampaignAsync();

        var response = await Client(Roles.Gestor).PostAsJsonAsync($"{Route}/{sourceId}/duplicate", new { name = "Volta às Aulas 2" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var copy = (await response.Content.ReadFromJsonAsync<CampaignResponse>())!;
        var source = (await Client().GetFromJsonAsync<CampaignResponse>($"{Route}/{sourceId}"))!;
        Assert.Equal($"/api/v1/campaigns/{copy.Id}", response.Headers.Location?.OriginalString);
        Assert.NotEqual(sourceId, copy.Id);
        Assert.Equal("Draft", copy.Status);
        Assert.Equal("Volta às Aulas 2", copy.Name);
        Assert.Equal(source.StartsAt, copy.StartsAt);
        Assert.Equal(source.EndsAt, copy.EndsAt);
        Assert.Equal(source.TotalVoucherLimit, copy.TotalVoucherLimit);
        Assert.Equal(source.AudienceType, copy.AudienceType);
        Assert.Equal(source.VoucherValidity, copy.VoucherValidity);
        Assert.Equal(source.MaxUsesPerVoucher, copy.MaxUsesPerVoucher);
        Assert.Equal(source.Items.Select(item => (item.TargetType, item.BenefitType, item.BenefitValue)),
            copy.Items.Select(item => (item.TargetType, item.BenefitType, item.BenefitValue)));
        Assert.DoesNotContain(copy.Items, item => source.Items.Any(original => original.Id == item.Id));
        Assert.Equal(source.Scopes.Select(scope => string.Join(',', scope.Channels)), copy.Scopes.Select(scope => string.Join(',', scope.Channels)));
        Assert.Equal("Scheduled", source.Status);
    }

    [Fact]
    public async Task Duplicate_WithoutBody_UsesCopyNameAndTheSourcePeriod()
    {
        var sourceId = await ReadyCampaign.CreateAsync(Client());

        var response = await Client().PostAsync($"{Route}/{sourceId}/duplicate", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var copy = (await response.Content.ReadFromJsonAsync<CampaignResponse>())!;
        Assert.StartsWith("Cópia de Campanha ", copy.Name);
        Assert.Equal(ReadyCampaign.StartsAt, copy.StartsAt);
    }

    [Fact]
    public async Task Duplicate_EndedCampaign_NeedsNewDates()
    {
        var sourceId = await ReadyCampaign.CreateAsync(Client());
        _factory.Clock.UtcNow = ReadyCampaign.EndsAt.AddDays(5);

        var samePeriod = await Client().PostAsync($"{Route}/{sourceId}/duplicate", null);
        var newPeriod = await Client().PostAsJsonAsync(
            $"{Route}/{sourceId}/duplicate", new { startsAt = "2026-12-01T08:00:00", endsAt = "2026-12-24T23:59:00" });

        await AssertBusinessRuleAsync(samePeriod, CampaignErrors.StartInPastCode, CampaignErrors.StartInPastMessage);
        Assert.Equal(HttpStatusCode.Created, newPeriod.StatusCode);
        var copy = (await newPeriod.Content.ReadFromJsonAsync<CampaignResponse>())!;
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.FromHours(-3)), copy.StartsAt);
    }

    [Fact]
    public async Task Duplicate_RecordsTheSourceInTheAudit()
    {
        var sourceId = await ReadyCampaign.CreateAsync(Client());

        var response = await Client().PostAsync($"{Route}/{sourceId}/duplicate", null);
        var copyId = (await response.Content.ReadFromJsonAsync<CampaignResponse>())!.Id;

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await context.AuditEntries.SingleAsync(item => item.CampaignId == copyId);
        Assert.Equal(AuditEventTypes.CampaignDuplicated, entry.EventType);
        Assert.Equal($$"""{"sourceCampaignId":"{{sourceId}}"}""", entry.Payload);
    }

    [Fact]
    public async Task Duplicate_FromAnotherNetwork_ReturnsForbidden()
    {
        var sourceId = await ReadyCampaign.CreateAsync(Client());

        var response = await Client(Roles.Analista, TestAuth.OtherNetworkId).PostAsync($"{Route}/{sourceId}/duplicate", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Pause_StartedScheduledCampaign_StoresPausedWithHistoryAndAudit()
    {
        var id = await ApprovedCampaignAsync();
        _factory.Clock.UtcNow = DuringPeriod;

        var response = await Client().PostAsync($"{Route}/{id}/pause", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Paused", (await response.Content.ReadFromJsonAsync<CampaignResponse>())!.Status);

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var statusEvent = await context.CampaignStatusEvents.SingleAsync(item => item.CampaignId == id && item.ToStatus == CampaignStatus.Paused);
        var entry = await context.AuditEntries.SingleAsync(item => item.CampaignId == id && item.EventType == AuditEventTypes.CampaignPaused);
        Assert.Equal(CampaignStatus.Active, statusEvent.FromStatus);
        Assert.Equal("""{"fromStatus":"Active","toStatus":"Paused"}""", entry.Payload);
    }

    [Fact]
    public async Task Pause_BeforeStartOrTwice_Returns422()
    {
        var id = await ApprovedCampaignAsync();

        var beforeStart = await Client().PostAsync($"{Route}/{id}/pause", null);
        _factory.Clock.UtcNow = DuringPeriod;
        (await Client().PostAsync($"{Route}/{id}/pause", null)).EnsureSuccessStatusCode();
        var twice = await Client().PostAsync($"{Route}/{id}/pause", null);

        await AssertBusinessRuleAsync(beforeStart, CampaignErrors.PauseNotActiveCode, CampaignErrors.PauseNotActiveMessage);
        await AssertBusinessRuleAsync(twice, CampaignErrors.PauseNotActiveCode, CampaignErrors.PauseNotActiveMessage);
    }

    [Fact]
    public async Task End_PausedCampaign_StoresEnded()
    {
        var id = await ApprovedCampaignAsync();
        _factory.Clock.UtcNow = DuringPeriod;
        (await Client().PostAsync($"{Route}/{id}/pause", null)).EnsureSuccessStatusCode();

        var response = await Client(Roles.Gestor).PostAsync($"{Route}/{id}/end", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ended", (await response.Content.ReadFromJsonAsync<CampaignResponse>())!.Status);
        var again = await Client().PostAsync($"{Route}/{id}/end", null);
        await AssertBusinessRuleAsync(again, CampaignErrors.EndNotAllowedCode, CampaignErrors.EndNotAllowedMessage);
    }

    [Fact]
    public async Task End_ScheduledOrDraft_Returns422()
    {
        var scheduled = await ApprovedCampaignAsync();
        var draft = await ReadyCampaign.CreateAsync(Client());

        await AssertBusinessRuleAsync(
            await Client().PostAsync($"{Route}/{scheduled}/end", null), CampaignErrors.EndNotAllowedCode, CampaignErrors.EndNotAllowedMessage);
        await AssertBusinessRuleAsync(
            await Client().PostAsync($"{Route}/{draft}/end", null), CampaignErrors.EndNotAllowedCode, CampaignErrors.EndNotAllowedMessage);
    }

    [Fact]
    public async Task PausedCampaign_ShowsEndedAfterThePeriod()
    {
        var id = await ApprovedCampaignAsync();
        _factory.Clock.UtcNow = DuringPeriod;
        (await Client().PostAsync($"{Route}/{id}/pause", null)).EnsureSuccessStatusCode();

        _factory.Clock.UtcNow = ReadyCampaign.EndsAt;

        Assert.Equal("Ended", (await Client().GetFromJsonAsync<CampaignResponse>($"{Route}/{id}"))!.Status);
    }

    [Fact]
    public async Task PauseAndEnd_ByAdministratorOrAnotherNetwork_ReturnForbidden()
    {
        var id = await ApprovedCampaignAsync();
        _factory.Clock.UtcNow = DuringPeriod;

        var admin = await Client(Roles.Administrador).PostAsync($"{Route}/{id}/pause", null);
        var otherNetwork = await Client(Roles.Gestor, TestAuth.OtherNetworkId).PostAsync($"{Route}/{id}/end", null);

        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, otherNetwork.StatusCode);
    }

    private async Task<Guid> ApprovedCampaignAsync()
    {
        var id = await ReadyCampaign.CreateSubmittedAsync(Client());
        (await Client(Roles.Gestor).PostAsync($"{Route}/{id}/approve", null)).EnsureSuccessStatusCode();
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
