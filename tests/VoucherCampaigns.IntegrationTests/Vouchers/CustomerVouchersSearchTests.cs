using System.Net;
using System.Net.Http.Json;
using VoucherCampaigns.IntegrationTests.Authentication;
using static VoucherCampaigns.IntegrationTests.Vouchers.MotorScenario;

namespace VoucherCampaigns.IntegrationTests.Vouchers;

/// <summary>Vouchers do cliente com o CPF no corpo (spec GH-066, D-9; SEC-06 da auditoria).</summary>
public class CustomerVouchersSearchTests : IClassFixture<ApiFactory>
{
    private const string SearchRoute = "/api/v1/satellite/customers/vouchers/search";
    private readonly ApiFactory _factory;

    public CustomerVouchersSearchTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task Search_ReturnsTheSameAsTheOldRoute_AndTheOldRouteIsMarkedDeprecated()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var document = TestCpf.New();
        var voucher = await AvailableVoucherAsync(satellite, scenario, document);

        var searched = await satellite.PostAsJsonAsync(SearchRoute, new { document });
        var old = await satellite.GetAsync($"/api/v1/satellite/customers/{document}/vouchers");

        searched.EnsureSuccessStatusCode();
        Assert.Equal(voucher.Id, Assert.Single((await searched.Content.ReadFromJsonAsync<List<VoucherResponseDto>>())!).Id);
        Assert.False(searched.Headers.Contains("Deprecation"));
        Assert.Equal("true", Assert.Single(old.Headers.GetValues("Deprecation")));
        Assert.Contains("customers/vouchers/search", Assert.Single(old.Headers.GetValues("Link")));
    }

    [Fact]
    public async Task Search_WithoutDocument_Returns400_AndWithInvalidDocument_Returns400()
    {
        var satellite = Satellite(_factory);

        var missing = await satellite.PostAsJsonAsync(SearchRoute, new { status = "Available" });
        var invalid = await satellite.PostAsJsonAsync(SearchRoute, new { document = "123" });

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await TestAuth.ReadProblemAsync(missing)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Search_WithoutTheIssueScope_IsForbidden()
    {
        var satellite = Satellite(_factory, scopes: ["vouchers:redeem"]);

        var response = await satellite.PostAsJsonAsync(SearchRoute, new { document = TestCpf.New() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
