using System.Net;
using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Domain.Vouchers;
using static VoucherCampaigns.IntegrationTests.Vouchers.MotorScenario;

namespace VoucherCampaigns.IntegrationTests.Vouchers;

/// <summary>US-05.4, cenário 4: a última utilização sob resgates simultâneos, no SQLite e no PostgreSQL.</summary>
public class RedemptionConcurrencyTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public RedemptionConcurrencyTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task Redeem_TwentySimultaneousPurchases_ConsumeExactlyTheLastUse()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var voucher = await AvailableVoucherAsync(satellite, scenario);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            RedeemAsync(satellite, Redeem(voucher.Id, scenario.StoreId, $"TRX-PARALELA-{index}"))));

        Assert.Single(results, result => result.Response.StatusCode == HttpStatusCode.OK);
        Assert.Equal(19, results.Count(result => result.Response.StatusCode == HttpStatusCode.UnprocessableEntity));

        var stored = await QueryAsync(_factory, db => db.Vouchers.AsNoTracking().SingleAsync(v => v.Id == voucher.Id));
        Assert.Equal(1, stored.UsesCount);
        Assert.Equal(VoucherStatus.Redeemed, stored.Status);
        Assert.Equal(1, await QueryAsync(_factory, db => db.VoucherEvents.CountAsync(e => e.VoucherId == voucher.Id && e.EventType == VoucherEventTypes.Redeemed)));
        Assert.Equal(1, await QueryAsync(_factory, db => db.Redemptions.CountAsync(r => r.VoucherId == voucher.Id)));
    }

    [Fact]
    public async Task Redeem_TheSameTransactionTenTimesAtOnce_RecordsASingleRedemption()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var satellite = Satellite(_factory);
        var voucher = await AvailableVoucherAsync(satellite, scenario);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            RedeemAsync(satellite, Redeem(voucher.Id, scenario.StoreId, "TRX-REPETIDA"))));

        var failures = new List<string>();
        foreach (var result in results.Where(result => result.Response.StatusCode != HttpStatusCode.OK))
        {
            failures.Add($"{(int)result.Response.StatusCode} {await result.Response.Content.ReadAsStringAsync()}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.Single(results.Select(result => result.Body!.RedemptionId).Distinct());
        var stored = await QueryAsync(_factory, db => db.Vouchers.AsNoTracking().SingleAsync(v => v.Id == voucher.Id));
        Assert.Equal(1, stored.UsesCount);
    }
}
