using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;

namespace VoucherCampaigns.IntegrationTests.Customers;

public class CustomerGroupEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public CustomerGroupEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(-3));
    }

    [Fact]
    public async Task Admin_CreatesGroup_AndAnalyst_ListsWithoutWriting()
    {
        var created = await Admin().PostAsJsonAsync("/api/v1/customer-groups", new { networkId = DemoDataSeeder.NetworkId, name = "Clube" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var group = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = group.GetProperty("id").GetGuid();

        var members = await Admin().PutAsJsonAsync($"/api/v1/customer-groups/{id}/members", new { documents = new[] { "111.444.777-35", "52998224725", "52998224725" } });
        Assert.Equal(HttpStatusCode.OK, members.StatusCode);
        Assert.Equal(2, (await members.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("memberCount").GetInt32());

        var list = await Analyst().GetFromJsonAsync<JsonElement>($"/api/v1/customer-groups?networkId={DemoDataSeeder.NetworkId}&activeOnly=true");
        Assert.Contains(list.EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);

        var denied = await Analyst().PostAsJsonAsync("/api/v1/customer-groups", new { networkId = DemoDataSeeder.NetworkId, name = "Outro" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task InvalidDocument_RejectsTheBatch()
    {
        var id = await CreateGroupAsync("Sócios");
        var response = await Admin().PutAsJsonAsync($"/api/v1/customer-groups/{id}/members", new { documents = new[] { "11144477735", "123" } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var again = await Admin().GetFromJsonAsync<JsonElement>($"/api/v1/customer-groups/{id}");
        Assert.Equal(0, again.GetProperty("memberCount").GetInt32());
    }

    [Fact]
    public async Task Preview_CountsTheUnion_AndCampaignRejectsWhenTheCeilingIsTooSmall()
    {
        var club = await CreateGroupAsync("Clube Norte");
        var partner = await CreateGroupAsync("Sócio Norte");
        await Admin().PutAsJsonAsync($"/api/v1/customer-groups/{club}/members", new { documents = new[] { "11144477735", "52998224725" } });
        await Admin().PutAsJsonAsync($"/api/v1/customer-groups/{partner}/members", new { documents = new[] { "52998224725", "39053344705" } });

        var preview = await Analyst().PostAsJsonAsync("/api/v1/customer-groups/preview", new { networkId = DemoDataSeeder.NetworkId, groupIds = new[] { club, partner } });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(3, (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("uniqueCustomerCount").GetInt32());

        var campaign = await Analyst().PostAsJsonAsync("/api/v1/campaigns", new
        {
            networkId = DemoDataSeeder.NetworkId,
            name = "Lote grupos",
            startsAt = "2026-10-01T08:00:00",
            endsAt = "2026-10-31T23:59:00",
        });
        campaign.EnsureSuccessStatusCode();
        var campaignId = (await campaign.Content.ReadFromJsonAsync<CampaignResponse>())!.Id;
        (await Analyst().PutAsJsonAsync($"/api/v1/campaigns/{campaignId}/quantities", new { totalVoucherLimit = 2, perCustomerVoucherLimit = 1 })).EnsureSuccessStatusCode();

        var linked = await Analyst().PutAsJsonAsync($"/api/v1/campaigns/{campaignId}/customer-groups", new { groupIds = new[] { club, partner } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, linked.StatusCode);
        Assert.Equal("CAMPAIGN_CUSTOMER_GROUPS_QUOTA", (await TestAuth.ReadProblemAsync(linked)).GetProperty("code").GetString());

        (await Analyst().PutAsJsonAsync($"/api/v1/campaigns/{campaignId}/quantities", new { totalVoucherLimit = 3, perCustomerVoucherLimit = 1 })).EnsureSuccessStatusCode();
        var stored = await Analyst().PutAsJsonAsync($"/api/v1/campaigns/{campaignId}/customer-groups", new { groupIds = new[] { club, partner } });
        stored.EnsureSuccessStatusCode();
        var body = await stored.Content.ReadFromJsonAsync<CampaignResponse>();
        Assert.Equal(3, body!.UniqueCustomerCount);
        Assert.Equal(2, body.CustomerGroups!.Count);

        var copy = await Analyst().PostAsJsonAsync($"/api/v1/campaigns/{campaignId}/duplicate", new { });
        copy.EnsureSuccessStatusCode();
        var duplicated = await copy.Content.ReadFromJsonAsync<CampaignResponse>();
        Assert.Equal(2, duplicated!.CustomerGroups!.Count);
        Assert.Equal(3, duplicated.UniqueCustomerCount);
    }

    private async Task<Guid> CreateGroupAsync(string name)
    {
        var response = await Admin().PostAsJsonAsync("/api/v1/customer-groups", new { networkId = DemoDataSeeder.NetworkId, name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private HttpClient Admin() => _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, Roles.Administrador));

    private HttpClient Analyst() => _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, Roles.Analista, DemoDataSeeder.NetworkId));
}
