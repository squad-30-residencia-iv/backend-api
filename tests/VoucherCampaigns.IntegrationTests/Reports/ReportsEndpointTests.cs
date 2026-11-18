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

    [Fact]
    public async Task Overview_FollowsTheDashboardRule_AndTheSlicesAddUpToTheIssued()
    {
        // O provisório em dia não é emitido; as fatias somam os emitidos (spec GH-050, D-3).
        await using var factory = new ApiFactory();
        var satellite = MotorScenario.Satellite(factory);
        var scenario = await MotorScenario.ApprovedCampaignAsync(factory);
        var customer = TestCpf.New();
        var used = await MotorScenario.AvailableVoucherAsync(satellite, scenario, customer);
        await MotorScenario.AvailableVoucherAsync(satellite, scenario, TestCpf.New());
        await MotorScenario.RedeemedAsync(satellite, MotorScenario.Redeem(used.Id, scenario.StoreId, document: customer));
        await MotorScenario.GrantedAsync(satellite, MotorScenario.Purchase(scenario.StoreId, document: TestCpf.New()));

        var body = await MotorScenario.User(factory).GetFromJsonAsync<OverviewDto>(
            $"/api/v1/reports/overview?networkId={DemoDataSeeder.NetworkId}&period=30d&campaignId={scenario.CampaignId}", Json);

        Assert.Equal((2, 1, 1, 0, 0), (body!.Kpis.Issued, body.Kpis.Redeemed, body.Kpis.Available, body.Kpis.Expired, body.Kpis.Cancelled));
        Assert.Equal(body.Kpis.Issued, body.StatusDistribution.Sum(slice => slice.Count));
    }

    [Fact]
    public async Task Overview_MonthlyChartCoversSixMonths_OutsideThePeriod()
    {
        await using var factory = new ApiFactory();
        var satellite = MotorScenario.Satellite(factory);
        var scenario = await MotorScenario.ApprovedCampaignAsync(factory);
        await MotorScenario.AvailableVoucherAsync(satellite, scenario);
        factory.Clock.UtcNow = MotorScenario.Now.AddDays(70);

        var body = await MotorScenario.User(factory).GetFromJsonAsync<OverviewDto>(
            $"/api/v1/reports/overview?networkId={DemoDataSeeder.NetworkId}&period=30d&campaignId={scenario.CampaignId}", Json);

        Assert.Equal(0, body!.Kpis.Issued);
        Assert.Equal(6, body.Monthly.Count);
        Assert.Equal(1, body.Monthly.Sum(point => point.Issued));
        Assert.Equal(1, body.Monthly.Sum(point => point.Expired));
    }

    private sealed record OverviewDto(
        KpisDto Kpis,
        IReadOnlyList<MonthlyDto> Monthly,
        IReadOnlyList<SliceDto> StatusDistribution);
    private sealed record MonthlyDto(string Month, int Issued, int Redeemed, int Expired);
    private sealed record SliceDto(string Status, int Count);
    private sealed record KpisDto(int Issued, int Redeemed, int Expired, int Available, int Cancelled, decimal? UtilizationRate);
}
