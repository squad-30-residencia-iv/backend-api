using System.Net;
using System.Net.Http.Json;
using VoucherCampaigns.IntegrationTests.Campaigns;
using static VoucherCampaigns.IntegrationTests.Vouchers.MotorScenario;

namespace VoucherCampaigns.IntegrationTests.Vouchers;

/// <summary>US-04.5, cenário 3: o último voucher sob requisições simultâneas (RNF02), no SQLite e no PostgreSQL.</summary>
public class ConcessionConcurrencyTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ConcessionConcurrencyTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task Grant_TwentySimultaneousPurchases_IssueExactlyTheLastVoucher()
    {
        var scenario = await ApprovedCampaignAsync(_factory, (analista, id) =>
            analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/quantities", new { totalVoucherLimit = 1 }));
        var satellite = Satellite(_factory);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            GrantAsync(satellite, Purchase(scenario.StoreId, document: $"{10_000_000_000 + index:D11}"))));

        Assert.All(results, result => Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode));
        var bodies = results.Select(result => result.Body!).ToList();
        Assert.Single(bodies, body => body.Status == "Provisional");
        Assert.Equal(19, bodies.Count(body => body.Status == "NotGranted" && body.Reason == "CampaignSoldOut"));

        var stored = await StoredCampaignAsync(_factory, scenario.CampaignId);
        Assert.Equal(1, stored.ReservedVouchers + stored.IssuedVouchers);
        Assert.Equal(1, Assert.Single(stored.Items).ReservedVouchers);
    }

    [Fact]
    public async Task ConfirmAndCancel_Simultaneously_OnlyOneWins()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var granted = await GrantedAsync(satellite, Purchase(scenario.StoreId));

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(index =>
            satellite.PostAsync($"{ConcessionsRoute}/{granted.ConcessionId}/{(index % 2 == 0 ? "confirm" : "cancel")}", null)));
        var state = await satellite.GetFromJsonAsync<ConcessionResponseDto>($"{ConcessionsRoute}/{granted.ConcessionId}");

        Assert.All(responses, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity }));
        var stored = await StoredCampaignAsync(_factory, scenario.CampaignId);
        Assert.Equal(0, stored.ReservedVouchers);
        Assert.Equal(state!.Status == "Confirmed" ? 1 : 0, stored.IssuedVouchers);
        Assert.Contains(state.Status, new[] { "Confirmed", "Cancelled" });
    }
}
