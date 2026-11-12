using System.Net;
using System.Net.Http.Json;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.IntegrationTests.Authentication;
using VoucherCampaigns.IntegrationTests.Campaigns;
using static VoucherCampaigns.IntegrationTests.Vouchers.MotorScenario;

namespace VoucherCampaigns.IntegrationTests.Vouchers;

/// <summary>Campanhas vigentes por API (US-07.1, spec GH-049 D-14).</summary>
public class SatelliteCampaignsEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public SatelliteCampaignsEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task GetActive_ListsOnlyEffectivelyActiveCampaigns_FilteredByStoreAndChannel()
    {
        var scenario = await ApprovedCampaignAsync(_factory);
        var otherStore = await NewStoreAsync(_factory, "Physical", "Virtual");
        var satellite = Satellite(_factory, scopes: [SatelliteScopes.ReadCampaigns]);

        var byStore = await satellite.GetFromJsonAsync<List<SatelliteCampaignResponse>>($"{SatelliteCampaignsRoute}?establishmentId={scenario.StoreId}");
        var campaign = Assert.Single(byStore!, item => item.Id == scenario.CampaignId);
        Assert.Equal(ReadyCampaign.StartsAt, campaign.StartsAt);
        Assert.Equal(ReadyCampaign.EndsAt, campaign.EndsAt);
        Assert.Equal("All", campaign.Audience);
        Assert.Equal(1, campaign.MaxUsesPerVoucher);
        Assert.False(campaign.Scope.Network);
        var entry = Assert.Single(campaign.Scope.Entries);
        Assert.Equal(scenario.StoreId, entry.EstablishmentId);
        Assert.Equal(["Physical"], entry.Channels);
        var target = Assert.Single(campaign.Items);
        Assert.Equal(("All", "Percentage", 10m), (target.TargetType, target.BenefitType, target.BenefitValue));

        var byStoreVirtual = await satellite.GetFromJsonAsync<List<SatelliteCampaignResponse>>($"{SatelliteCampaignsRoute}?establishmentId={scenario.StoreId}&channel=Virtual");
        var byOtherStore = await satellite.GetFromJsonAsync<List<SatelliteCampaignResponse>>($"{SatelliteCampaignsRoute}?establishmentId={otherStore}");
        var all = (await satellite.GetFromJsonAsync<List<SatelliteCampaignResponse>>(SatelliteCampaignsRoute))!;
        Assert.DoesNotContain(byStoreVirtual!, item => item.Id == scenario.CampaignId);
        Assert.DoesNotContain(byOtherStore!, item => item.Id == scenario.CampaignId);
        Assert.Contains(all, item => item.Id == scenario.CampaignId);
        Assert.Equal(all.OrderBy(item => item.EndsAt).ThenBy(item => item.Name).ThenBy(item => item.Id).Select(item => item.Id), all.Select(item => item.Id));

        // Pausada sai da lista; antes do início (agendada) também.
        (await User(_factory, Roles.Gestor).PostAsync($"{ReadyCampaign.Route}/{scenario.CampaignId}/pause", null)).EnsureSuccessStatusCode();
        var afterPause = await satellite.GetFromJsonAsync<List<SatelliteCampaignResponse>>($"{SatelliteCampaignsRoute}?establishmentId={scenario.StoreId}");
        Assert.DoesNotContain(afterPause!, item => item.Id == scenario.CampaignId);

        var scheduled = await ApprovedCampaignAsync(_factory);
        _factory.Clock.UtcNow = Setup;
        var beforeStart = await satellite.GetFromJsonAsync<List<SatelliteCampaignResponse>>($"{SatelliteCampaignsRoute}?establishmentId={scheduled.StoreId}");
        Assert.Empty(beforeStart!);
    }

    [Fact]
    public async Task GetActive_RejectsUnknownStoreInvalidChannelAndMissingScope()
    {
        var satellite = Satellite(_factory, scopes: [SatelliteScopes.ReadCampaigns]);

        var unknownStore = await satellite.GetAsync($"{SatelliteCampaignsRoute}?establishmentId={Guid.NewGuid()}");
        var invalidChannel = await satellite.GetAsync($"{SatelliteCampaignsRoute}?channel=Drive");
        var noScope = await Satellite(_factory, scopes: [SatelliteScopes.IssueVouchers]).GetAsync(SatelliteCampaignsRoute);
        var user = await User(_factory).GetAsync(SatelliteCampaignsRoute);

        Assert.Equal(HttpStatusCode.NotFound, unknownStore.StatusCode);
        Assert.Equal("A loja informada não existe nesta rede.", (await TestAuth.ReadProblemAsync(unknownStore)).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, invalidChannel.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noScope.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, user.StatusCode);
    }
}
