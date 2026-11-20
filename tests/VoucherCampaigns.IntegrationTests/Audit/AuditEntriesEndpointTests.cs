using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Campaigns;
using VoucherCampaigns.IntegrationTests.Vouchers;

namespace VoucherCampaigns.IntegrationTests.Audit;

public class AuditEntriesEndpointTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task List_AfterSubmit_ContainsCampaignSubmitted()
    {
        await using var factory = new ApiFactory();
        factory.Clock.UtcNow = MotorScenario.Setup;
        var analista = MotorScenario.User(factory);
        var id = await ReadyCampaign.CreateAsync(analista);
        (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/scopes", new
        {
            scopes = new[] { new { establishmentId = (Guid?)null, regionId = (Guid?)null, channels = new[] { "Physical" } } }
        })).EnsureSuccessStatusCode();
        (await analista.PostAsync($"{ReadyCampaign.Route}/{id}/submit", null)).EnsureSuccessStatusCode();

        var response = await analista.GetAsync($"/api/v1/audit-entries?networkId={DemoDataSeeder.NetworkId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = await response.Content.ReadFromJsonAsync<List<AuditDto>>(Json);
        Assert.Contains(list!, entry => entry.EventType == AuditEventTypes.CampaignSubmitted && entry.CampaignId == id);
    }

    [Fact]
    public async Task GetById_ReturnsDetail()
    {
        await using var factory = new ApiFactory();
        factory.Clock.UtcNow = MotorScenario.Setup;
        var analista = MotorScenario.User(factory);
        var id = await ReadyCampaign.CreateAsync(analista);
        (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/scopes", new
        {
            scopes = new[] { new { establishmentId = (Guid?)null, regionId = (Guid?)null, channels = new[] { "Physical" } } }
        })).EnsureSuccessStatusCode();
        (await analista.PostAsync($"{ReadyCampaign.Route}/{id}/submit", null)).EnsureSuccessStatusCode();

        var list = await analista.GetFromJsonAsync<List<AuditDto>>(
            $"/api/v1/audit-entries?networkId={DemoDataSeeder.NetworkId}&campaignId={id}", Json);
        var entry = Assert.Single(list!, item => item.EventType == AuditEventTypes.CampaignSubmitted);

        var detailResponse = await analista.GetAsync($"/api/v1/audit-entries/{entry.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var document = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal(AuditEventTypes.CampaignSubmitted, document.RootElement.GetProperty("eventType").GetString());
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        await using var factory = new ApiFactory();
        var response = await MotorScenario.User(factory).GetAsync($"/api/v1/audit-entries/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record AuditDto(Guid Id, string EventType, Guid? CampaignId);
}
