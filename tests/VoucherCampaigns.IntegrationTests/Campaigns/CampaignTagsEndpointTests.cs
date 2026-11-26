using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;
using VoucherCampaigns.IntegrationTests.Vouchers;
using static VoucherCampaigns.IntegrationTests.Vouchers.MotorScenario;

namespace VoucherCampaigns.IntegrationTests.Campaigns;

/// <summary>Tags de campanha (spec GH-077): lista fechada por rede, até 5 por campanha, em qualquer status, com filtro.</summary>
public class CampaignTagsEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string TagsRoute = "/api/v1/campaign-tags";

    [Fact]
    public async Task Administrator_ManagesTheNetworkTags_AnalystAndManagerOnlyRead()
    {
        var admin = User(factory, Roles.Administrador);
        var name = Unique("Black Friday");

        var created = await CreateTagAsync(name);
        Assert.Equal((name, true, 0), (created.Name, created.IsActive, created.CampaignCount));

        var repeated = await admin.PostAsJsonAsync(TagsRoute, new { networkId = DemoDataSeeder.NetworkId, name = $"  {name.ToUpperInvariant()} " });
        Assert.Equal("CAMPAIGN_TAG_NAME_TAKEN", (await TestAuth.ReadProblemAsync(repeated)).GetProperty("code").GetString());

        var otherNetwork = await NewNetworkAsync();
        var sameNameElsewhere = await admin.PostAsJsonAsync(TagsRoute, new { networkId = otherNetwork, name });
        Assert.Equal(HttpStatusCode.Created, sameNameElsewhere.StatusCode);

        var renamed = await admin.PutAsJsonAsync($"{TagsRoute}/{created.Id}", new { name = name + " 2026", isActive = true });
        Assert.Equal(name + " 2026", (await renamed.Content.ReadFromJsonAsync<CampaignTagResponse>())!.Name);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{TagsRoute}/{created.Id}")).StatusCode);
        var inactive = (await User(factory).GetFromJsonAsync<List<CampaignTagResponse>>($"{TagsRoute}?networkId={DemoDataSeeder.NetworkId}&activeOnly=false"))!;
        Assert.Contains(inactive, tag => tag.Id == created.Id && !tag.IsActive);
        var reactivated = await admin.PutAsJsonAsync($"{TagsRoute}/{created.Id}", new { name = name + " 2026", isActive = true });
        Assert.True((await reactivated.Content.ReadFromJsonAsync<CampaignTagResponse>())!.IsActive);

        foreach (var role in new[] { Roles.Analista, Roles.Gestor })
        {
            var reader = User(factory, role);
            Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"{TagsRoute}?networkId={DemoDataSeeder.NetworkId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(TagsRoute, new { networkId = DemoDataSeeder.NetworkId, name = Unique("Natal") })).StatusCode);
        }

        await AssertValidationAsync(await admin.PostAsJsonAsync(TagsRoute, new { networkId = DemoDataSeeder.NetworkId, name = new string('x', 41) }), "Name");
        await AssertValidationAsync(await admin.PostAsJsonAsync(TagsRoute, new { networkId = DemoDataSeeder.NetworkId, name = "  " }), "Name");
    }

    [Fact]
    public async Task PutTags_ReplacesTheList_AndValidatesEachTag()
    {
        factory.Clock.UtcNow = Setup;
        var analista = User(factory);
        var id = await ReadyCampaign.CreateAsync(analista);
        var first = await CreateTagAsync(Unique("Dia das Mães"));
        var second = await CreateTagAsync(Unique("Black Friday"));

        var body = await PutTagsOkAsync(id, first.Id, second.Id, first.Id);
        Assert.Equal(new[] { second.Name, first.Name }.OrderBy(name => name.ToUpperInvariant(), StringComparer.Ordinal), body.Tags!.Select(tag => tag.Name));

        var six = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        await AssertValidationAsync(await PutTagsAsync(id, six), "TagIds");
        await AssertValidationAsync(await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/tags", new { }), "TagIds");

        var missing = Guid.NewGuid();
        var notFound = await TestAuth.ReadProblemAsync(await PutTagsAsync(id, first.Id, missing));
        Assert.Equal("CAMPAIGN_TAG_NOT_FOUND", notFound.GetProperty("code").GetString());
        Assert.Equal([missing], notFound.GetProperty("tagIds").EnumerateArray().Select(item => item.GetGuid()));

        var foreign = await CreateTagAsync(Unique("Outra rede"), await NewNetworkAsync());
        Assert.Equal("CAMPAIGN_TAG_OUTSIDE_NETWORK", (await TestAuth.ReadProblemAsync(await PutTagsAsync(id, foreign.Id))).GetProperty("code").GetString());

        var off = await CreateTagAsync(Unique("Inativa"));
        (await User(factory, Roles.Administrador).DeleteAsync($"{TagsRoute}/{off.Id}")).EnsureSuccessStatusCode();
        Assert.Equal("CAMPAIGN_TAG_INACTIVE", (await TestAuth.ReadProblemAsync(await PutTagsAsync(id, off.Id))).GetProperty("code").GetString());

        var cleared = await PutTagsOkAsync(id);
        Assert.Empty(cleared.Tags!);
    }

    [Fact]
    public async Task Tags_ChangeInAnyStatus_InactiveOnesStayVisible_AndAreAuditedOnlyWhenChanged()
    {
        var scenario = await ApprovedCampaignAsync(factory);
        var tag = await CreateTagAsync(Unique("Natal"));

        // D-3: campanha aprovada e ativa aceita a troca de tags.
        Assert.Equal("Active", await CampaignStatusAsync(factory, scenario.CampaignId));
        await PutTagsOkAsync(scenario.CampaignId, tag.Id);
        await PutTagsOkAsync(scenario.CampaignId, tag.Id);

        (await User(factory, Roles.Administrador).DeleteAsync($"{TagsRoute}/{tag.Id}")).EnsureSuccessStatusCode();
        var campaign = (await User(factory).GetFromJsonAsync<CampaignResponse>($"{ReadyCampaign.Route}/{scenario.CampaignId}"))!;
        var kept = Assert.Single(campaign.Tags!);
        Assert.Equal((tag.Id, false), (kept.Id, kept.IsActive));

        // A inativada que a campanha já tem pode ficar na lista reenviada.
        await PutTagsOkAsync(scenario.CampaignId, tag.Id);

        var sections = await QueryAsync(factory, db => db.AuditEntries
            .Where(entry => entry.CampaignId == scenario.CampaignId && entry.EventType == AuditEventTypes.CampaignUpdated)
            .Select(entry => entry.Payload!)
            .ToListAsync());
        Assert.Single(sections, payload => payload.Contains("\"section\":\"tags\""));
    }

    [Fact]
    public async Task ListFiltersByTag_WithStatusAndPaging_AndDuplicateCopiesActiveTags()
    {
        factory.Clock.UtcNow = Setup;
        var analista = User(factory);
        var tag = await CreateTagAsync(Unique("Filtro"));
        var tagged = new List<Guid>();

        for (var index = 0; index < 3; index++)
        {
            var id = await ReadyCampaign.CreateAsync(analista);
            await PutTagsOkAsync(id, tag.Id);
            tagged.Add(id);
        }

        await ReadyCampaign.CreateAsync(analista);
        var baseRoute = $"{ReadyCampaign.Route}?networkId={DemoDataSeeder.NetworkId}&tagId={tag.Id}";

        var all = (await analista.GetFromJsonAsync<List<CampaignResponse>>(baseRoute))!;
        Assert.Equal(tagged.OrderBy(id => id), all.Select(campaign => campaign.Id).OrderBy(id => id));
        Assert.All(all, campaign => Assert.Contains(campaign.Tags!, item => item.Id == tag.Id));

        var page = await analista.GetAsync($"{baseRoute}&status=Draft&page=1&pageSize=2");
        Assert.Equal(2, (await page.Content.ReadFromJsonAsync<List<CampaignResponse>>())!.Count);
        Assert.Equal("3", Assert.Single(page.Headers.GetValues("X-Total-Count")));

        var duplicate = await analista.PostAsJsonAsync($"{ReadyCampaign.Route}/{tagged[0]}/duplicate", new { });
        duplicate.EnsureSuccessStatusCode();
        Assert.Equal([tag.Id], (await duplicate.Content.ReadFromJsonAsync<CampaignResponse>())!.Tags!.Select(item => item.Id));

        await AssertValidationAsync(await analista.GetAsync($"{ReadyCampaign.Route}?networkId={DemoDataSeeder.NetworkId}&tagId=abc"), "tagId");
    }

    [Fact]
    public async Task SatelliteCampaigns_DoNotExposeTags()
    {
        var tag = await CreateTagAsync(Unique("PDV"));
        var scenario = await ApprovedCampaignAsync(factory, (analista, id) => analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/tags", new { tagIds = new[] { tag.Id } }));

        var listed = await Satellite(factory).GetStringAsync($"{SatelliteCampaignsRoute}?establishmentId={scenario.StoreId}");

        Assert.Contains(scenario.CampaignId.ToString(), listed);
        Assert.DoesNotContain("\"tags\"", listed);
    }

    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}"[..Math.Min(name.Length + 9, 40)];

    private async Task<CampaignTagResponse> CreateTagAsync(string name, Guid? networkId = null)
    {
        var response = await User(factory, Roles.Administrador)
            .PostAsJsonAsync(TagsRoute, new { networkId = networkId ?? DemoDataSeeder.NetworkId, name });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<CampaignTagResponse>())!;
    }

    private async Task<Guid> NewNetworkAsync()
    {
        var response = await User(factory, Roles.Administrador)
            .PostAsJsonAsync("/api/v1/networks", new { name = Unique("Rede"), timeZone = "America/Sao_Paulo" });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<NetworkResponse>())!.Id;
    }

    private Task<HttpResponseMessage> PutTagsAsync(Guid campaignId, params Guid[] tagIds) =>
        User(factory).PutAsJsonAsync($"{ReadyCampaign.Route}/{campaignId}/tags", new { tagIds });

    private async Task<CampaignResponse> PutTagsOkAsync(Guid campaignId, params Guid[] tagIds)
    {
        var response = await PutTagsAsync(campaignId, tagIds);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<CampaignResponse>())!;
    }

    private static async Task AssertValidationAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response);
        Assert.True(problem.GetProperty("errors").EnumerateObject().Any(error => error.Name.Equals(field, StringComparison.OrdinalIgnoreCase)), problem.ToString());
    }
}
