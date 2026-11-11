using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Infrastructure.Persistence;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;

namespace VoucherCampaigns.IntegrationTests.Campaigns;

/// <summary>Escopo da campanha (US-02.3): lojas 001 e 002 operam o físico; a 003, o virtual.</summary>
public class CampaignScopesEndpointTests : IClassFixture<ApiFactory>
{
    private const string Route = ReadyCampaign.Route;

    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(-3));

    private readonly ApiFactory _factory;

    public CampaignScopesEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task Put_StoresNetworkRegionAndStoresWithNamesInTheInformedOrder()
    {
        var id = await CreateDraftAsync();

        var response = await Client().PutAsJsonAsync($"{Route}/{id}/scopes", new
        {
            scopes = new object[]
            {
                new { establishmentId = DemoDataSeeder.StoreOnlineId, channels = new[] { "virtual" } },
                new { regionId = DemoDataSeeder.RegionId, channels = new[] { "Virtual", "Physical" } },
                new { channels = new[] { "Physical" } },
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var scopes = (await response.Content.ReadFromJsonAsync<CampaignResponse>())!.Scopes;
        Assert.Equal(3, scopes.Count);
        Assert.Equal(new CampaignScopeResponse(null, DemoDataSeeder.StoreOnlineId, "Loja Online", "003", ["Virtual"]), scopes[0], ScopeComparer);
        Assert.Equal(new CampaignScopeResponse(DemoDataSeeder.RegionId, null, "Região Norte", null, ["Physical", "Virtual"]), scopes[1], ScopeComparer);
        Assert.Equal(new CampaignScopeResponse(null, null, null, null, ["Physical"]), scopes[2], ScopeComparer);

        var reread = await Client().GetFromJsonAsync<CampaignResponse>($"{Route}/{id}");
        Assert.Equal(scopes, reread!.Scopes, ScopeComparer);
    }

    [Fact]
    public async Task Put_ReplacesThePreviousList()
    {
        var id = await CreateDraftAsync();
        await PutScopesAsync(id, new { channels = new[] { "Physical", "Virtual" } });

        var response = await PutScopesAsync(id, new { establishmentId = DemoDataSeeder.StoreCenterId, channels = new[] { "Physical" } });

        var scope = Assert.Single((await response.Content.ReadFromJsonAsync<CampaignResponse>())!.Scopes);
        Assert.Equal(DemoDataSeeder.StoreCenterId, scope.EstablishmentId);
    }

    [Fact]
    public async Task Put_ChannelTheStoreDoesNotOperate_Returns422WithStoreAndChannel()
    {
        var id = await CreateDraftAsync();

        var response = await PutScopesAsync(id, new { establishmentId = DemoDataSeeder.StoreCenterId, channels = new[] { "Physical", "Virtual" } });

        await AssertBusinessRuleAsync(response, CampaignErrors.ScopeChannelNotEnabledCode, "A loja 001 não opera o canal virtual.");
    }

    [Fact]
    public async Task Put_UnknownInactiveOrForeignLocations_Return422()
    {
        var id = await CreateDraftAsync();
        var (inactiveStoreId, foreignStoreId, foreignRegionId, inactiveRegionId) = await AddLocationsAsync();

        await AssertBusinessRuleAsync(
            await PutScopesAsync(id, new { establishmentId = Guid.NewGuid(), channels = new[] { "Physical" } }),
            CampaignErrors.ScopeEstablishmentNotFoundCode, CampaignErrors.ScopeEstablishmentNotFoundMessage);
        await AssertBusinessRuleAsync(
            await PutScopesAsync(id, new { establishmentId = inactiveStoreId, channels = new[] { "Physical" } }),
            CampaignErrors.ScopeEstablishmentInactiveCode, CampaignErrors.ScopeEstablishmentInactiveMessage);
        await AssertBusinessRuleAsync(
            await PutScopesAsync(id, new { establishmentId = foreignStoreId, channels = new[] { "Physical" } }),
            CampaignErrors.ScopeOutsideNetworkCode, CampaignErrors.ScopeOutsideNetworkMessage);
        await AssertBusinessRuleAsync(
            await PutScopesAsync(id, new { regionId = foreignRegionId, channels = new[] { "Physical" } }),
            CampaignErrors.ScopeOutsideNetworkCode, CampaignErrors.ScopeOutsideNetworkMessage);
        await AssertBusinessRuleAsync(
            await PutScopesAsync(id, new { regionId = Guid.NewGuid(), channels = new[] { "Physical" } }),
            CampaignErrors.ScopeRegionNotFoundCode, OrganizationErrors.EstablishmentRegionNotFoundMessage);
        await AssertBusinessRuleAsync(
            await PutScopesAsync(id, new { regionId = inactiveRegionId, channels = new[] { "Physical" } }),
            CampaignErrors.ScopeRegionInactiveCode, OrganizationErrors.EstablishmentRegionInactiveMessage);

        var stored = await Client().GetFromJsonAsync<CampaignResponse>($"{Route}/{id}");
        Assert.Empty(stored!.Scopes);
    }

    [Fact]
    public async Task Put_SameLocationAndChannelTwice_Returns422()
    {
        var id = await CreateDraftAsync();

        var response = await PutScopesAsync(
            id,
            new { establishmentId = DemoDataSeeder.StoreCenterId, channels = new[] { "Physical" } },
            new { establishmentId = DemoDataSeeder.StoreCenterId, channels = new[] { "Physical" } });

        await AssertBusinessRuleAsync(response, CampaignErrors.ScopeDuplicatedCode, CampaignErrors.ScopeDuplicatedMessage);
    }

    public static TheoryData<string, string, string> InvalidBodies() => new()
    {
        { """{"scopes":[]}""", "Scopes", CampaignContract.ScopesRequired },
        { "{}", "Scopes", CampaignContract.ScopesRequired },
        { $$"""{"scopes":[{"regionId":"{{DemoDataSeeder.RegionId}}","establishmentId":"{{DemoDataSeeder.StoreCenterId}}","channels":["Physical"]}]}""", "Scopes[0].EstablishmentId", CampaignContract.ScopeRegionAndEstablishment },
        { """{"scopes":[{"channels":[]}]}""", "Scopes[0].Channels", CampaignContract.ScopeChannelsRequired },
        { """{"scopes":[{"channels":["Drive"]}]}""", "Scopes[0].Channels", CampaignContract.ScopeChannelInvalid },
        { """{"scopes":[{"channels":["Physical","physical"]}]}""", "Scopes[0].Channels", CampaignContract.ScopeChannelsRepeated },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Put_WithInvalidContract_ReturnsValidationProblem(string json, string field, string message)
    {
        var id = await CreateDraftAsync();

        var response = await Client().PutAsync(
            $"{Route}/{id}/scopes", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response);
        Assert.Equal("VALIDATION_ERROR", problem.GetProperty("code").GetString());
        Assert.Contains(message, problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(error => error.GetString()));
    }

    [Fact]
    public async Task Put_OutsideDraft_Returns422()
    {
        var id = await ReadyCampaign.CreateSubmittedAsync(Client());

        var response = await PutScopesAsync(id, new { channels = new[] { "Physical" } });

        await AssertBusinessRuleAsync(response, CampaignErrors.NotDraftCode, CampaignErrors.NotDraftMessage);
    }

    [Fact]
    public async Task Put_FromAnotherNetwork_ReturnsForbidden()
    {
        var id = await CreateDraftAsync();

        var response = await Client(TestAuth.OtherNetworkId).PutAsJsonAsync(
            $"{Route}/{id}/scopes", new { scopes = new[] { new { channels = new[] { "Physical" } } } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Submit_WithoutScope_Returns422()
    {
        var id = await ReadyCampaign.CreateAsync(Client());
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.CampaignScopes.RemoveRange(context.CampaignScopes.Where(item => item.CampaignId == id));
            await context.SaveChangesAsync();
        }

        var response = await Client().PostAsync($"{Route}/{id}/submit", null);

        await AssertBusinessRuleAsync(response, CampaignErrors.SubmitScopeRequiredCode, CampaignErrors.SubmitScopeRequiredMessage);
    }

    private async Task<Guid> CreateDraftAsync()
    {
        var created = await Client().PostAsJsonAsync(Route, new
        {
            networkId = DemoDataSeeder.NetworkId,
            name = $"Campanha {Guid.NewGuid():N}",
            startsAt = "2026-10-01T08:00:00",
            endsAt = "2026-10-31T23:59:00",
        });
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<CampaignResponse>())!.Id;
    }

    private Task<HttpResponseMessage> PutScopesAsync(Guid id, params object[] scopes) =>
        Client().PutAsJsonAsync($"{Route}/{id}/scopes", new { scopes });

    /// <summary>Loja inativa na região de demonstração e rede, região e loja de outra rede.</summary>
    private async Task<(Guid InactiveStore, Guid ForeignStore, Guid ForeignRegion, Guid InactiveRegion)> AddLocationsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var inactiveStore = Store(DemoDataSeeder.RegionId, isActive: true);
        inactiveStore.IsActive = false;
        var inactiveRegion = new Region { NetworkId = DemoDataSeeder.NetworkId, Name = $"Inativa {Guid.NewGuid():N}", IsActive = false };
        var foreignNetwork = new Network { Name = $"Outra {Guid.NewGuid():N}" };
        var foreignRegion = new Region { NetworkId = foreignNetwork.Id, Name = "Sul" };
        var foreignStore = Store(foreignRegion.Id, isActive: true);

        context.AddRange(foreignNetwork, foreignRegion, inactiveRegion, inactiveStore, foreignStore);
        await context.SaveChangesAsync();

        return (inactiveStore.Id, foreignStore.Id, foreignRegion.Id, inactiveRegion.Id);
    }

    private static Establishment Store(Guid regionId, bool isActive)
    {
        var store = new Establishment { RegionId = regionId, Code = Guid.NewGuid().ToString("N")[..8], Name = "Loja extra", IsActive = isActive };
        store.EnableChannels([ChannelType.Physical], DateTime.UtcNow);
        return store;
    }

    private HttpClient Client(Guid? networkId = null) =>
        _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, Roles.Analista, networkId));

    private static async Task AssertBusinessRuleAsync(HttpResponseMessage response, string code, string message)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal(message, problem.GetProperty("detail").GetString());
    }

    private static readonly ScopeResponseComparer ScopeComparer = new();

    private sealed class ScopeResponseComparer : IEqualityComparer<CampaignScopeResponse>
    {
        public bool Equals(CampaignScopeResponse? x, CampaignScopeResponse? y) =>
            x is not null && y is not null
            && x.RegionId == y.RegionId && x.EstablishmentId == y.EstablishmentId
            && x.Name == y.Name && x.Code == y.Code && x.Channels.SequenceEqual(y.Channels);

        public int GetHashCode(CampaignScopeResponse obj) => HashCode.Combine(obj.RegionId, obj.EstablishmentId);
    }
}
