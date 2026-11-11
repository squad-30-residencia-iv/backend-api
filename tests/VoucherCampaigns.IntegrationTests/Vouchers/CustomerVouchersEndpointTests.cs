using System.Net;
using System.Net.Http.Json;
using VoucherCampaigns.IntegrationTests.Authentication;
using static VoucherCampaigns.IntegrationTests.Vouchers.MotorScenario;

namespace VoucherCampaigns.IntegrationTests.Vouchers;

/// <summary>US-04.8 e US-07.4: os vouchers do cliente pela API, com o status efetivo.</summary>
public class CustomerVouchersEndpointTests : IClassFixture<ApiFactory>
{
    private const string Route = "/api/v1/satellite/customers";

    private readonly ApiFactory _factory;

    public CustomerVouchersEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task List_ReturnsTheCustomerVouchersWithTheEffectiveStatusAndFilters()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        const string document = "32132132100";

        var provisional = await GrantedAsync(satellite, Purchase(scenario.StoreId, document: document));
        var confirmed = await GrantedAsync(satellite, Purchase(scenario.StoreId, document: document));
        (await satellite.PostAsync($"{ConcessionsRoute}/{confirmed.ConcessionId}/confirm", null)).EnsureSuccessStatusCode();
        var cancelled = await GrantedAsync(satellite, Purchase(scenario.StoreId, document: document));
        (await satellite.PostAsync($"{ConcessionsRoute}/{cancelled.ConcessionId}/cancel", null)).EnsureSuccessStatusCode();

        var all = await satellite.GetFromJsonAsync<List<VoucherResponseDto>>($"{Route}/321.321.321-00/vouchers");
        var available = await satellite.GetFromJsonAsync<List<VoucherResponseDto>>($"{Route}/{document}/vouchers?status=available");
        var expired = await satellite.GetFromJsonAsync<List<VoucherResponseDto>>($"{Route}/{document}/vouchers?status=Expired");

        Assert.Equal(new[] { confirmed.Voucher!.Id, provisional.Voucher!.Id }.Order(), all!.Select(voucher => voucher.Id).Order());
        Assert.Equal(1, all!.Single(voucher => voucher.Id == confirmed.Voucher.Id).RemainingUses);
        Assert.Equal(confirmed.Voucher.Id, Assert.Single(available!).Id);
        Assert.Empty(expired!);

        _factory.Clock.UtcNow = Now.AddDays(31);
        var later = await satellite.GetFromJsonAsync<List<VoucherResponseDto>>($"{Route}/{document}/vouchers");
        _factory.Clock.UtcNow = Now;

        Assert.Equal("Expired", later!.Single(voucher => voucher.Id == confirmed.Voucher.Id).Status);
        Assert.DoesNotContain(later!, voucher => voucher.Id == provisional.Voucher.Id);
    }

    [Fact]
    public async Task List_ForACustomerWithoutVouchers_ReturnsEmpty()
    {
        var vouchers = await Satellite(_factory).GetFromJsonAsync<List<VoucherResponseDto>>($"{Route}/00000000191/vouchers");

        Assert.Empty(vouchers!);
    }

    [Theory]
    [InlineData("1234567890", null)]
    [InlineData("12345678901", "Discarded")]
    [InlineData("12345678901", "Qualquer")]
    public async Task List_WithInvalidDocumentOrStatus_ReturnsValidationProblem(string document, string? status)
    {
        var response = await Satellite(_factory).GetAsync($"{Route}/{document}/vouchers{(status is null ? "" : $"?status={status}")}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response);
        Assert.Equal("VALIDATION_ERROR", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task List_DoesNotShowVouchersOfAnotherNetwork()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        await GrantedAsync(satellite, Purchase(scenario.StoreId, document: "77788899900"));

        var other = await Satellite(_factory, networkId: TestAuth.OtherNetworkId).GetFromJsonAsync<List<VoucherResponseDto>>($"{Route}/77788899900/vouchers");

        Assert.Empty(other!);
    }
}
