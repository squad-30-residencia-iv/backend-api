using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Vouchers;

namespace VoucherCampaigns.IntegrationTests.Reports;

public class ReportsEndpointTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Overview_EmptyNetwork_ReturnsZeroKpis()
    {
        await using var factory = new ApiFactory();
        var response = await MotorScenario.User(factory).GetAsync(
            $"/api/v1/reports/overview?networkId={DemoDataSeeder.NetworkId}&period=30d");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<OverviewDto>(Json);
        Assert.NotNull(body);
        Assert.Equal(0, body!.Kpis.Issued);
        Assert.Null(body.Kpis.UtilizationRate);
    }

    [Fact]
    public async Task Overview_WithVoucher_CountsIssued()
    {
        await using var factory = new ApiFactory();
        var satellite = MotorScenario.Satellite(factory);
        var scenario = await MotorScenario.ApprovedCampaignAsync(factory);
        await MotorScenario.AvailableVoucherAsync(satellite, scenario);

        var response = await MotorScenario.User(factory).GetAsync(
            $"/api/v1/reports/overview?networkId={DemoDataSeeder.NetworkId}&period=6m");
        var body = await response.Content.ReadFromJsonAsync<OverviewDto>(Json);

        Assert.True(body!.Kpis.Issued >= 1);
        Assert.True(body.Kpis.Available >= 1);
    }

    [Fact]
    public async Task Catalog_ReturnsCampaigns()
    {
        await using var factory = new ApiFactory();
        var scenario = await MotorScenario.ApprovedCampaignAsync(factory);

        var response = await MotorScenario.User(factory).GetAsync(
            $"/api/v1/reports/catalog?networkId={DemoDataSeeder.NetworkId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var campaigns = document.RootElement.GetProperty("campaigns");
        Assert.Contains(campaigns.EnumerateArray(), item => item.GetProperty("value").GetGuid() == scenario.CampaignId);
    }

    private sealed record OverviewDto(KpisDto Kpis);
    private sealed record KpisDto(int Issued, int Redeemed, int Expired, int Available, int Cancelled, decimal? UtilizationRate);
}
