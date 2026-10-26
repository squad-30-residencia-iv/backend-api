using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Infrastructure.Persistence;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;

namespace VoucherCampaigns.IntegrationTests.Campaigns;

public class GetCampaignsEndpointTests : IClassFixture<ApiFactory>
{
    private const string Route = "/api/v1/campaigns";

    // 28/09/2026, meio-dia em Brasília.
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(-3));

    private readonly ApiFactory _factory;

    public GetCampaignsEndpointTests(ApiFactory factory)
    {
        _factory = factory;

        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task GetAll_Scenario1_ListsNetworkCampaignsNewestFirst()
    {
        var networkId = await AddNetworkAsync("America/Manaus");
        var client = Client(networkId: networkId);

        var first = await CreateAsync(client, networkId, "Volta às Aulas");
        _factory.Clock.UtcNow = Now.AddMinutes(1);
        var second = await CreateAsync(client, networkId, "Black Friday");

        var response = await client.GetAsync($"{Route}?networkId={networkId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var campaigns = await response.Content.ReadFromJsonAsync<List<CampaignResponse>>();

        Assert.NotNull(campaigns);
        Assert.Equal([second.Id, first.Id], campaigns.Select(campaign => campaign.Id));

        var listed = campaigns[1];
        Assert.Equal("Draft", listed.Status);
        Assert.Equal(0, listed.IssuedVouchers);
        Assert.Null(listed.TotalVoucherLimit);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(-4)), listed.StartsAt);
        Assert.Equal(TimeSpan.FromHours(-4), listed.EndsAt.Offset);
    }

    [Fact]
    public async Task GetAll_ByGestor_ReturnsOk()
    {
        var response = await Client(Roles.Gestor).GetAsync($"{Route}?networkId={DemoDataSeeder.NetworkId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("draft")]
    public async Task GetAll_FilteredByStatus_ReturnsOnlyMatchingCampaigns(string status)
    {
        var networkId = await AddNetworkAsync("America/Sao_Paulo");
        var draft = await CreateAsync(Client(networkId: networkId), networkId, "Rascunho");
        var active = await AddCampaignAsync(networkId, CampaignStatus.Active);

        var drafts = await GetListAsync(networkId, $"&status={status}");
        var actives = await GetListAsync(networkId, "&status=Active");
        var paused = await GetListAsync(networkId, "&status=Paused");

        Assert.Equal([draft.Id], drafts.Select(campaign => campaign.Id));
        Assert.Equal([active], actives.Select(campaign => campaign.Id));
        Assert.Empty(paused);
    }

    [Fact]
    public async Task GetAll_DoesNotListCampaignsOfAnotherNetwork()
    {
        var otherNetworkId = await AddNetworkAsync("America/Sao_Paulo");
        var foreign = await CreateAsync(Client(networkId: otherNetworkId), otherNetworkId, "De outra rede");

        var campaigns = await GetListAsync(DemoDataSeeder.NetworkId);

        Assert.DoesNotContain(campaigns, campaign => campaign.Id == foreign.Id);
        Assert.All(campaigns, campaign => Assert.Equal(DemoDataSeeder.NetworkId, campaign.NetworkId));
    }

    [Theory]
    [InlineData("Xpto")]
    [InlineData("1")]
    public async Task GetAll_WithUnknownStatus_ReturnsValidationProblem(string status)
    {
        var response = await Client().GetAsync($"{Route}?networkId={DemoDataSeeder.NetworkId}&status={status}");

        await AssertValidationAsync(response, "Status", CampaignContract.StatusInvalid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?networkId=00000000-0000-0000-0000-000000000000")]
    public async Task GetAll_WithoutNetwork_ReturnsValidationProblem(string query)
    {
        var response = await Client().GetAsync($"{Route}{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetAll_ForNetworkOutsideUserScope_ReturnsScopeForbidden()
    {
        var response = await Client().GetAsync($"{Route}?networkId={TestAuth.OtherNetworkId}");

        await AssertScopeForbiddenAsync(response);
    }

    [Fact]
    public async Task GetAll_ForNetworkInScopeButNotRegistered_ReturnsNotFound()
    {
        var response = await Client(networkId: TestAuth.OtherNetworkId).GetAsync($"{Route}?networkId={TestAuth.OtherNetworkId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NOT_FOUND", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetById_ReturnsSameBodyAsCreate()
    {
        var client = Client();
        var created = await client.PostAsJsonAsync(Route, Request(DemoDataSeeder.NetworkId, "Detalhe"));
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();

        var response = await client.GetAsync(created.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(createdBody.GetRawText(), (await response.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        Assert.Equal(0, createdBody.GetProperty("issuedVouchers").GetInt32());
        Assert.Equal(JsonValueKind.Null, createdBody.GetProperty("totalVoucherLimit").ValueKind);
    }

    [Fact]
    public async Task GetById_ReturnsDatesInNetworkTimeZone()
    {
        var networkId = await AddNetworkAsync("America/Manaus");
        var client = Client(networkId: networkId);
        var created = await CreateAsync(client, networkId, "Manaus");

        var campaign = await client.GetFromJsonAsync<CampaignResponse>($"{Route}/{created.Id}");

        Assert.NotNull(campaign);
        Assert.Equal("America/Manaus", campaign.TimeZone);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(-4)), campaign.StartsAt);
    }

    [Fact]
    public async Task GetById_ForCampaignOfAnotherNetwork_ReturnsScopeForbidden()
    {
        var otherNetworkId = await AddNetworkAsync("America/Sao_Paulo");
        var foreign = await CreateAsync(Client(networkId: otherNetworkId), otherNetworkId, "De outra rede");

        var response = await Client().GetAsync($"{Route}/{foreign.Id}");

        await AssertScopeForbiddenAsync(response);
    }

    [Fact]
    public async Task GetById_WithUnknownId_ReturnsNotFound()
    {
        var response = await Client().GetAsync($"{Route}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await TestAuth.ReadProblemAsync(response);

        Assert.Equal("NOT_FOUND", problem.GetProperty("code").GetString());
        Assert.Equal(CampaignContract.CampaignNotFound, problem.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("?networkId=0199e3a0-0000-7000-8000-000000000001")]
    [InlineData("/0199e3a0-0000-7000-8000-000000000001")]
    public async Task Get_WithoutToken_ReturnsUnauthorized(string suffix)
    {
        var response = await _factory.CreateClient().GetAsync($"{Route}{suffix}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("?networkId=0199e3a0-0000-7000-8000-000000000001")]
    [InlineData("/0199e3a0-0000-7000-8000-000000000001")]
    public async Task Get_ByPlatformAdmin_ReturnsForbiddenRole(string suffix)
    {
        var response = await Client(Roles.Administrador).GetAsync($"{Route}{suffix}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("AUTH_FORBIDDEN_ROLE", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
    }

    // Token com a hora real: o relógio fixo da Api não vale para a validação do JWT.
    private HttpClient Client(string role = Roles.Analista, Guid? networkId = null) =>
        _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, role, networkId));

    private static object Request(Guid networkId, string name) => new
    {
        networkId,
        name,
        startsAt = "2026-10-01T08:00:00",
        endsAt = "2026-10-31T23:59:00"
    };

    private static async Task<CampaignResponse> CreateAsync(HttpClient client, Guid networkId, string name)
    {
        var response = await client.PostAsJsonAsync(Route, Request(networkId, name));
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CampaignResponse>())!;
    }

    private async Task<List<CampaignResponse>> GetListAsync(Guid networkId, string extraQuery = "")
    {
        var response = await Client(networkId: networkId).GetAsync($"{Route}?networkId={networkId}{extraQuery}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<List<CampaignResponse>>())!;
    }

    /// <summary>Rede própria do teste, para a listagem não depender das campanhas criadas por outros testes.</summary>
    private async Task<Guid> AddNetworkAsync(string timeZone)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var network = new Network { Name = $"Rede {Guid.NewGuid():N}", TimeZone = timeZone };
        context.Networks.Add(network);
        await context.SaveChangesAsync();

        return network.Id;
    }

    /// <summary>Grava direto no banco uma campanha em outro status, pois ainda não há rota de submissão/aprovação.</summary>
    private async Task<Guid> AddCampaignAsync(Guid networkId, CampaignStatus status)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var campaign = Campaign.CreateDraft(
            networkId,
            "Em vigor",
            null,
            new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 1, 2, 59, 0, DateTimeKind.Utc),
            Now.UtcDateTime);
        campaign.Status = status;

        context.Campaigns.Add(campaign);
        await context.SaveChangesAsync();

        return campaign.Id;
    }

    private static async Task AssertValidationAsync(HttpResponseMessage response, string field, string message)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await TestAuth.ReadProblemAsync(response);

        Assert.Equal("VALIDATION_ERROR", problem.GetProperty("code").GetString());
        Assert.Contains(message, problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(error => error.GetString()));
    }

    private static async Task AssertScopeForbiddenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var problem = await TestAuth.ReadProblemAsync(response);

        Assert.Equal("SCOPE_FORBIDDEN", problem.GetProperty("code").GetString());
        Assert.Equal("Escopo organizacional fora da sua permissão de acesso", problem.GetProperty("detail").GetString());
    }
}
