using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;

namespace VoucherCampaigns.IntegrationTests.Vouchers;

public class PortalVouchersEndpointTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task List_AfterConfirm_ReturnsVoucherMasked()
    {
        await using var factory = new ApiFactory();
        var satellite = MotorScenario.Satellite(factory);
        var scenario = await MotorScenario.ApprovedCampaignAsync(factory);
        var voucher = await MotorScenario.AvailableVoucherAsync(satellite, scenario);

        var response = await MotorScenario.User(factory).GetAsync(
            $"/api/v1/vouchers?networkId={DemoDataSeeder.NetworkId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = await response.Content.ReadFromJsonAsync<List<PortalVoucherDto>>(Json);
        var item = Assert.Single(list!, entry => entry.Id == voucher.Id);
        Assert.Equal(voucher.Code, item.Code);
        Assert.Equal("Available", item.Status);
        Assert.Equal("***.456.789-**", item.CustomerDocumentMasked);
        Assert.Null(item.CustomerName);
    }

    [Fact]
    public async Task GetById_ReturnsEvents()
    {
        await using var factory = new ApiFactory();
        var satellite = MotorScenario.Satellite(factory);
        var scenario = await MotorScenario.ApprovedCampaignAsync(factory);
        var voucher = await MotorScenario.AvailableVoucherAsync(satellite, scenario);

        var response = await MotorScenario.User(factory).GetAsync($"/api/v1/vouchers/{voucher.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await response.Content.ReadFromJsonAsync<PortalVoucherDetailDto>(Json);
        Assert.NotNull(detail);
        Assert.Equal(voucher.Id, detail!.Voucher.Id);
        Assert.Contains(detail.Events, entry => entry.Type is "Issued" or "Confirmed");
        Assert.False(string.IsNullOrWhiteSpace(detail.Benefit));
    }

    [Fact]
    public async Task List_FilterByCampaign_ReturnsOnlyThatCampaign()
    {
        await using var factory = new ApiFactory();
        var satellite = MotorScenario.Satellite(factory);
        var first = await MotorScenario.ApprovedCampaignAsync(factory);
        var second = await MotorScenario.ApprovedCampaignAsync(factory);
        await MotorScenario.AvailableVoucherAsync(satellite, first);
        await MotorScenario.AvailableVoucherAsync(satellite, second);

        var response = await MotorScenario.User(factory).GetAsync(
            $"/api/v1/vouchers?networkId={DemoDataSeeder.NetworkId}&campaignId={first.CampaignId}");
        var list = await response.Content.ReadFromJsonAsync<List<PortalVoucherDto>>(Json);

        Assert.All(list!, item => Assert.Equal(first.CampaignId, item.CampaignId));
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        await using var factory = new ApiFactory();
        var response = await MotorScenario.User(factory).GetAsync($"/api/v1/vouchers/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_WithoutAuth_Returns401()
    {
        await using var factory = new ApiFactory();
        var response = await factory.CreateClient().GetAsync($"/api/v1/vouchers?networkId={DemoDataSeeder.NetworkId}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CampaignDetail_IncludesRedeemedCounter()
    {
        await using var factory = new ApiFactory();
        var satellite = MotorScenario.Satellite(factory);
        var scenario = await MotorScenario.ApprovedCampaignAsync(factory);
        var voucher = await MotorScenario.AvailableVoucherAsync(satellite, scenario);
        await MotorScenario.RedeemedAsync(satellite, MotorScenario.Redeem(voucher.Id, scenario.StoreId));

        var response = await MotorScenario.User(factory).GetAsync($"/api/v1/campaigns/{scenario.CampaignId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, document.RootElement.GetProperty("redeemedVouchers").GetInt32());
        Assert.Equal(0, document.RootElement.GetProperty("expiredVouchers").GetInt32());
    }

    [Fact]
    public async Task CampaignCounters_FollowTheDashboardRule_InTheList_TheDetail_AndTheStatusChanges()
    {
        // Resgatado = emitido com ao menos uma utilização; expirado = vencido sem utilização (spec GH-050, D-3).
        await using var factory = new ApiFactory();
        var satellite = MotorScenario.Satellite(factory);
        var scenario = await MotorScenario.ApprovedCampaignAsync(factory);
        var customer = TestCpf.New();
        var used = await MotorScenario.AvailableVoucherAsync(satellite, scenario, customer);
        await MotorScenario.AvailableVoucherAsync(satellite, scenario, TestCpf.New());
        await MotorScenario.RedeemedAsync(satellite, MotorScenario.Redeem(used.Id, scenario.StoreId, document: customer));
        await MotorScenario.GrantedAsync(satellite, MotorScenario.Purchase(scenario.StoreId, document: TestCpf.New()));
        var analista = MotorScenario.User(factory);

        var paused = await analista.PostAsync($"/api/v1/campaigns/{scenario.CampaignId}/pause", null);
        paused.EnsureSuccessStatusCode();
        Assert.Equal((1, 0), await CountersAsync(paused));

        factory.Clock.UtcNow = MotorScenario.Now.AddDays(40);
        Assert.Equal((1, 1), await CountersAsync(await analista.GetAsync($"/api/v1/campaigns/{scenario.CampaignId}")));

        using var list = JsonDocument.Parse(await analista.GetStringAsync($"/api/v1/campaigns?networkId={DemoDataSeeder.NetworkId}"));
        var listed = list.RootElement.EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == scenario.CampaignId);
        Assert.Equal(1, listed.GetProperty("redeemedVouchers").GetInt32());
        Assert.Equal(1, listed.GetProperty("expiredVouchers").GetInt32());
    }

    [Fact]
    public async Task Search_ByCpf_NeedsTheWholeNumber()
    {
        await using var factory = new ApiFactory();
        var satellite = MotorScenario.Satellite(factory);
        var scenario = await MotorScenario.ApprovedCampaignAsync(factory);
        var voucher = await MotorScenario.AvailableVoucherAsync(satellite, scenario);
        var analista = MotorScenario.User(factory);

        var whole = await analista.GetFromJsonAsync<List<PortalVoucherDto>>(
            $"/api/v1/vouchers?networkId={DemoDataSeeder.NetworkId}&search={Uri.EscapeDataString(MotorScenario.Document)}", Json);
        Assert.Contains(whole!, item => item.Id == voucher.Id);

        var part = MotorScenario.DocumentDigits[..10];
        var partial = await analista.GetFromJsonAsync<List<PortalVoucherDto>>(
            $"/api/v1/vouchers?networkId={DemoDataSeeder.NetworkId}&search={part}", Json);
        Assert.DoesNotContain(partial!, item => item.Id == voucher.Id && !item.Code.Contains(part, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Detail_AfterTheCampaignIsCancelled_LabelsTheEventAsCampaignCancelled()
    {
        await using var factory = new ApiFactory();
        var satellite = MotorScenario.Satellite(factory);
        var scenario = await MotorScenario.ApprovedCampaignAsync(factory);
        var voucher = await MotorScenario.AvailableVoucherAsync(satellite, scenario);
        var analista = MotorScenario.User(factory);
        (await analista.PostAsJsonAsync($"/api/v1/campaigns/{scenario.CampaignId}/cancel", new { reason = "Erro de configuração" }))
            .EnsureSuccessStatusCode();

        var detail = await analista.GetFromJsonAsync<PortalVoucherDetailDto>($"/api/v1/vouchers/{voucher.Id}", Json);

        Assert.Equal("Cancelled", detail!.Voucher.Status);
        Assert.Equal("Campanha cancelada", detail.Events.First(entry => entry.Type == "Cancelled").Result);
    }

    private static async Task<(int Redeemed, int Expired)> CountersAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (document.RootElement.GetProperty("redeemedVouchers").GetInt32(), document.RootElement.GetProperty("expiredVouchers").GetInt32());
    }

    private sealed record PortalVoucherDto(
        Guid Id,
        string Code,
        Guid CampaignId,
        string CampaignName,
        string? CustomerName,
        string CustomerDocumentMasked,
        string Status);

    private sealed record PortalVoucherDetailDto(
        PortalVoucherDto Voucher,
        string Benefit,
        IReadOnlyList<PortalVoucherEventDto> Events);

    private sealed record PortalVoucherEventDto(Guid Id, string Type, string Result);
}
