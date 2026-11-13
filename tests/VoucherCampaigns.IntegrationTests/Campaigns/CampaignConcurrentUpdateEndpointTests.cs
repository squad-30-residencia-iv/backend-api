using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Campaigns;
using static VoucherCampaigns.IntegrationTests.Vouchers.MotorScenario;

namespace VoucherCampaigns.IntegrationTests.Campaigns;

/// <summary>
/// Trava de gravação simultânea da campanha (spec GH-047, PA-10): edições do rascunho e transições de status
/// só gravam se a campanha não mudou desde a leitura; a segunda recebe 422 CAMPAIGN_CONCURRENT_UPDATE.
/// </summary>
public class CampaignConcurrentUpdateEndpointTests : IClassFixture<ApiFactory>
{

    private readonly ApiFactory _factory;

    public CampaignConcurrentUpdateEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Setup;
    }

    [Fact]
    public async Task PutItems_ConcurrentEdits_NeverMergeTheItems()
    {
        var id = await ReadyCampaign.CreateAsync(User(_factory));

        var responses = await Task.WhenAll(Enumerable.Range(1, 4).Select(value =>
            User(_factory).PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/items",
                new { items = new[] { new { targetType = "All", benefitType = "Percentage", benefitValue = value * 5 } } })));

        await AssertOneWinnerAsync(responses, allowOthersToSucceed: true, CampaignErrors.ConcurrentUpdateCode);
        Assert.Equal(1, await QueryAsync(_factory, db => db.CampaignItems.CountAsync(item => item.CampaignId == id)));
    }

    [Fact]
    public async Task PutConditions_ConcurrentEdits_NeverMergeTheConditions()
    {
        var id = await ReadyCampaign.CreateAsync(User(_factory));

        var responses = await Task.WhenAll(Enumerable.Range(1, 4).Select(value =>
            User(_factory).PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/conditions",
                new { conditions = new[] { new { type = "PurchaseTotal", minAmount = value * 10m } }, allowUseInSamePurchase = false })));

        await AssertOneWinnerAsync(responses, allowOthersToSucceed: true, CampaignErrors.ConcurrentUpdateCode);
        Assert.Equal(1, await QueryAsync(_factory, db => db.CampaignConditions.CountAsync(condition => condition.CampaignId == id)));
    }

    [Fact]
    public async Task Approve_ConcurrentRequests_ApproveOnlyOnce()
    {
        var id = await ReadyCampaign.CreateSubmittedAsync(User(_factory));

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            User(_factory, Roles.Gestor).PostAsync($"{ReadyCampaign.Route}/{id}/approve", null)));

        await AssertOneWinnerAsync(responses, allowOthersToSucceed: false, CampaignErrors.ConcurrentUpdateCode, CampaignErrors.DecisionNotPendingCode);
        Assert.Equal(1, await QueryAsync(_factory, db => db.AuditEntries
            .CountAsync(entry => entry.CampaignId == id && entry.EventType == AuditEventTypes.CampaignApproved)));
        Assert.Equal(1, await QueryAsync(_factory, db => db.CampaignStatusEvents
            .CountAsync(statusEvent => statusEvent.CampaignId == id && statusEvent.ToStatus == CampaignStatus.Scheduled)));
    }

    [Fact]
    public async Task SequentialEdits_WithTheClockStopped_AllSucceed()
    {
        var analista = User(_factory);
        var id = await ReadyCampaign.CreateAsync(analista);

        for (var limit = 10; limit <= 40; limit += 10)
        {
            (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/quantities", new { totalVoucherLimit = limit })).EnsureSuccessStatusCode();
        }

        Assert.Equal(40, await QueryAsync(_factory, db => db.Campaigns.Where(campaign => campaign.Id == id)
            .Select(campaign => campaign.TotalVoucherLimit).SingleAsync()));
    }

    /// <summary>
    /// Pelo menos uma requisição grava. As outras ou perdem a corrida (422 de concorrência) ou, se chegaram depois do
    /// commit, leem o estado novo: na edição gravam por cima; na aprovação caem na regra de status (422).
    /// </summary>
    private static async Task AssertOneWinnerAsync(HttpResponseMessage[] responses, bool allowOthersToSucceed, params string[] refusalCodes)
    {
        Assert.All(responses, response => Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.UnprocessableEntity, response.StatusCode.ToString()));

        var succeeded = responses.Count(response => response.StatusCode == HttpStatusCode.OK);
        Assert.True(allowOthersToSucceed ? succeeded >= 1 : succeeded == 1, $"{succeeded} gravaram");

        foreach (var refused in responses.Where(response => response.StatusCode == HttpStatusCode.UnprocessableEntity))
        {
            var code = (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
            Assert.Contains(code, refusalCodes);
        }
    }
}
