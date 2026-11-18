using System.Net;
using System.Net.Http.Json;
using System.Text;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Dashboard;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;
using VoucherCampaigns.IntegrationTests.Campaigns;
using VoucherCampaigns.IntegrationTests.Vouchers;
using static VoucherCampaigns.IntegrationTests.Vouchers.MotorScenario;

namespace VoucherCampaigns.IntegrationTests.Dashboard;

/// <summary>Indicadores, filtros, lista e exportação (US-03.1, 03.2, 03.3, 03.5; spec GH-050).</summary>
public class DashboardEndpointTests : IClassFixture<ApiFactory>
{
    private const string DashboardRoute = "/api/v1/dashboard";
    private readonly ApiFactory _factory;

    public DashboardEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task CampaignDashboard_CountsEachIssuedVoucherInExactlyOneBucket()
    {
        var scenario = await ApprovedCampaignAsync(_factory, (analista, id) =>
            analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/usage-restrictions", new { maxUsesPerVoucher = 2 }));
        var satellite = Satellite(_factory);
        var analista = User(_factory);

        // Um voucher emitido em 10/10 (vence antes) e quatro em 15/10; dois resgatados (um parcial), um provisório pendente.
        var early = await AvailableVoucherAsync(satellite, scenario, Cpf(1));
        _factory.Clock.UtcNow = Now.AddDays(5);
        var partial = await AvailableVoucherAsync(satellite, scenario, Cpf(2));
        var full = await AvailableVoucherAsync(satellite, scenario, Cpf(3));
        await AvailableVoucherAsync(satellite, scenario, Cpf(4));
        await AvailableVoucherAsync(satellite, scenario, Cpf(5));
        await RedeemedAsync(satellite, Redeem(partial.Id, scenario.StoreId, "TRX-D1", Cpf(2)));
        await RedeemedAsync(satellite, Redeem(full.Id, scenario.StoreId, "TRX-D2", Cpf(3)));
        await RedeemedAsync(satellite, Redeem(full.Id, scenario.StoreId, "TRX-D3", Cpf(3)));
        await GrantedAsync(satellite, Purchase(scenario.StoreId, document: Cpf(6)));

        var pending = await analista.GetFromJsonAsync<CampaignDashboardResponse>($"{ReadyCampaign.Route}/{scenario.CampaignId}/dashboard");

        Assert.Equal("Active", pending!.Status);
        Assert.Equal(new CampaignIndicatorsResponse(5, 1, 2, 0, 0, 3, 0.4000m, 3, 60.00m), pending.Indicators);

        // Depois da validade do primeiro: expirado sem uso; a reserva pendente venceu e sai de "reservados".
        _factory.Clock.UtcNow = early.ValidUntil.AddSeconds(1);
        var later = await analista.GetFromJsonAsync<CampaignDashboardResponse>($"{ReadyCampaign.Route}/{scenario.CampaignId}/dashboard");

        Assert.Equal(new CampaignIndicatorsResponse(5, 0, 2, 1, 0, 2, 0.4000m, 3, 60.00m), later!.Indicators);
        var indicators = later.Indicators;
        Assert.Equal(indicators.Issued, indicators.Redeemed + indicators.ExpiredUnused + indicators.CancelledUnused + indicators.Available);
    }

    [Fact]
    public async Task CampaignDashboard_WithoutMovement_IsZeroWithNullRate()
    {
        var scenario = await ApprovedCampaignAsync(_factory);

        var dashboard = await User(_factory).GetFromJsonAsync<CampaignDashboardResponse>($"{ReadyCampaign.Route}/{scenario.CampaignId}/dashboard");

        Assert.Equal(CampaignIndicatorsResponse.Zero, dashboard!.Indicators);
        Assert.Null(dashboard.Indicators.UtilizationRate);
        Assert.Equal(ReadyCampaign.StartsAt, dashboard.StartsAt);
    }

    [Fact]
    public async Task Filters_ByStoreRegionCategoryAndPeriod_AreCumulative()
    {
        var allTarget = await ApprovedCampaignAsync(_factory);
        var fashion = await ApprovedCampaignAsync(_factory, (analista, id) =>
            analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/items", new { items = new[] { new { targetType = "Category", categoryId = DemoDataSeeder.FashionCategoryId, benefitType = "Percentage", benefitValue = 10 } } }));
        var satellite = Satellite(_factory);
        await AvailableVoucherAsync(satellite, allTarget, Cpf(11));
        await AvailableVoucherAsync(satellite, allTarget, Cpf(12));
        await AvailableVoucherAsync(satellite, fashion, Cpf(13));
        var analista = User(_factory);

        async Task<int> IssuedAsync(string query, Guid campaignId) =>
            (await analista.GetFromJsonAsync<CampaignDashboardResponse>($"{ReadyCampaign.Route}/{campaignId}/dashboard{query}"))!.Indicators.Issued;

        Assert.Equal(2, await IssuedAsync($"?establishmentId={allTarget.StoreId}", allTarget.CampaignId));
        Assert.Equal(0, await IssuedAsync($"?establishmentId={fashion.StoreId}", allTarget.CampaignId));
        Assert.Equal(2, await IssuedAsync($"?regionId={DemoDataSeeder.RegionId}", allTarget.CampaignId));
        Assert.Equal(1, await IssuedAsync($"?categoryId={DemoDataSeeder.FashionCategoryId}", fashion.CampaignId));
        Assert.Equal(0, await IssuedAsync($"?categoryId={DemoDataSeeder.ShoesCategoryId}", fashion.CampaignId));
        Assert.Equal(0, await IssuedAsync($"?categoryId={DemoDataSeeder.FashionCategoryId}", allTarget.CampaignId));
        Assert.Equal(2, await IssuedAsync("?from=2026-10-10&to=2026-10-10", allTarget.CampaignId));
        Assert.Equal(0, await IssuedAsync("?from=2026-10-11", allTarget.CampaignId));
        Assert.Equal(2, await IssuedAsync($"?from=2026-10-01&to=2026-10-31&establishmentId={allTarget.StoreId}&regionId={DemoDataSeeder.RegionId}", allTarget.CampaignId));

        var inverted = await analista.GetAsync($"{ReadyCampaign.Route}/{allTarget.CampaignId}/dashboard?from=2026-10-20&to=2026-10-10");
        var unknownStore = await analista.GetAsync($"{ReadyCampaign.Route}/{allTarget.CampaignId}/dashboard?establishmentId={Guid.NewGuid()}");
        var unknownRegion = await analista.GetAsync($"{ReadyCampaign.Route}/{allTarget.CampaignId}/dashboard?regionId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownStore.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownRegion.StatusCode);

        var list = await analista.GetFromJsonAsync<NetworkDashboardResponse>($"{DashboardRoute}?networkId={DemoDataSeeder.NetworkId}&establishmentId={allTarget.StoreId}");
        Assert.Equal(allTarget.StoreId, list!.Filters.EstablishmentId);
        Assert.Equal("Loja do teste", list.Filters.EstablishmentName);
        Assert.Equal(2, list.Campaigns.Single(campaign => campaign.CampaignId == allTarget.CampaignId).Indicators.Issued);
        Assert.Equal(0, list.Campaigns.Single(campaign => campaign.CampaignId == fashion.CampaignId).Indicators.Issued);
    }

    [Fact]
    public async Task NetworkDashboard_ListsEveryStatusWithTotals_AndFiltersByStatus()
    {
        var analista = User(_factory);
        _factory.Clock.UtcNow = Setup;
        var draftId = await ReadyCampaign.CreateAsync(analista);
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var voucher = await AvailableVoucherAsync(satellite, scenario, Cpf(21));
        await RedeemedAsync(satellite, Redeem(voucher.Id, scenario.StoreId, "TRX-D4", Cpf(21)));
        (await User(_factory, Roles.Gestor).PostAsync($"{ReadyCampaign.Route}/{scenario.CampaignId}/pause", null)).EnsureSuccessStatusCode();

        var all = await analista.GetFromJsonAsync<NetworkDashboardResponse>($"{DashboardRoute}?networkId={DemoDataSeeder.NetworkId}");

        Assert.Equal("Draft", all!.Campaigns.Single(campaign => campaign.CampaignId == draftId).Status);
        var paused = all.Campaigns.Single(campaign => campaign.CampaignId == scenario.CampaignId);
        Assert.Equal("Paused", paused.Status);
        Assert.Equal(1, paused.Indicators.Redeemed);
        Assert.Equal("AllCustomers", paused.Audience);
        Assert.True(all.Totals.Issued >= 1);
        Assert.Equal(all.Campaigns.Sum(campaign => campaign.Indicators.Redeemed), all.Totals.Redeemed);
        Assert.Equal(all.Campaigns.OrderByDescending(campaign => campaign.EndsAt).ThenBy(campaign => campaign.Name).ThenBy(campaign => campaign.CampaignId).Select(campaign => campaign.CampaignId), all.Campaigns.Select(campaign => campaign.CampaignId));

        var onlyPaused = await analista.GetFromJsonAsync<NetworkDashboardResponse>($"{DashboardRoute}?networkId={DemoDataSeeder.NetworkId}&status=Paused");
        Assert.All(onlyPaused!.Campaigns, campaign => Assert.Equal("Paused", campaign.Status));
        Assert.Contains(onlyPaused.Campaigns, campaign => campaign.CampaignId == scenario.CampaignId);

        var none = await analista.GetFromJsonAsync<NetworkDashboardResponse>($"{DashboardRoute}?networkId={DemoDataSeeder.NetworkId}&status=Cancelled");
        Assert.Empty(none!.Campaigns);
        Assert.Equal(CampaignIndicatorsResponse.Zero, none.Totals);
    }

    [Fact]
    public async Task Export_ReturnsTheSameViewAsCsv_AndRefusesAnEmptyView()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        await AvailableVoucherAsync(satellite, scenario, Cpf(31));
        var analista = User(_factory);
        var view = await analista.GetFromJsonAsync<NetworkDashboardResponse>($"{DashboardRoute}?networkId={DemoDataSeeder.NetworkId}");
        var name = view!.Campaigns.Single(campaign => campaign.CampaignId == scenario.CampaignId).Name;

        var response = await analista.GetAsync($"{DashboardRoute}/export?networkId={DemoDataSeeder.NetworkId}&establishmentId={scenario.StoreId}");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var text = Encoding.UTF8.GetString(bytes);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        Assert.Contains("Filtros;loja Loja do teste", text);
        Assert.Contains("Campanha;Status;Início;Fim;Tipo de cliente;Emitidos;", text);
        Assert.Contains($"{name};Active;01/10/2026 08:00;31/10/2026 23:59;AllCustomers;1;0;0;0;0;1;0,00%;0;0,00", text);
        Assert.Contains("\r\nTotal;", text);

        var empty = await analista.GetAsync($"{DashboardRoute}/export?networkId={DemoDataSeeder.NetworkId}&status=Cancelled");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
        Assert.Equal("EXPORT_EMPTY", (await TestAuth.ReadProblemAsync(empty)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Routes_AreForAnalystsAndManagersOfTheNetworkOnly()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var admin = User(_factory, Roles.Administrador);
        var otherNetwork = _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, Roles.Gestor, networkId: Guid.NewGuid()));
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"{DashboardRoute}?networkId={DemoDataSeeder.NetworkId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherNetwork.GetAsync($"{DashboardRoute}?networkId={DemoDataSeeder.NetworkId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherNetwork.GetAsync($"{ReadyCampaign.Route}/{scenario.CampaignId}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherNetwork.GetAsync($"{ReadyCampaign.Route}/{scenario.CampaignId}/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{DashboardRoute}?networkId={DemoDataSeeder.NetworkId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await User(_factory, Roles.Gestor).GetAsync($"{ReadyCampaign.Route}/{scenario.CampaignId}/dashboard")).StatusCode);
    }

    private static string Cpf(int index) => $"{30_000_000_000 + index * 7919L:D11}";
}
