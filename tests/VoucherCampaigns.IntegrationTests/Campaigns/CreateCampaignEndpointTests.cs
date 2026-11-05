using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Infrastructure.Persistence;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;

namespace VoucherCampaigns.IntegrationTests.Campaigns;

public class CreateCampaignEndpointTests : IClassFixture<ApiFactory>
{
    private const string Route = "/api/v1/campaigns";

    private readonly ApiFactory _factory;

    public CreateCampaignEndpointTests(ApiFactory factory)
    {
        _factory = factory;

        // "Hoje" dos cenários do backlog: 28/09/2026, meio-dia em Brasília.
        _factory.Clock.UtcNow = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(-3));
    }

    [Fact]
    public async Task Create_Scenario1_RegistersDraftCampaignWithUniqueId()
    {
        var response = await Client().PostAsJsonAsync(Route, Request("Volta às Aulas", "2026-10-01T08:00:00", "2026-10-31T23:59:00"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var campaign = await response.Content.ReadFromJsonAsync<CampaignResponse>();

        Assert.NotNull(campaign);
        Assert.NotEqual(Guid.Empty, campaign.Id);
        Assert.Equal($"/api/v1/campaigns/{campaign.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal("Draft", campaign.Status);
        Assert.Equal(DemoDataSeeder.NetworkId, campaign.NetworkId);
        Assert.Equal("America/Sao_Paulo", campaign.TimeZone);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(-3)), campaign.StartsAt);
        Assert.Equal(TimeSpan.FromHours(-3), campaign.StartsAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 23, 59, 0, TimeSpan.FromHours(-3)), campaign.EndsAt);

        await using var scope = _factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Campaigns.SingleAsync(c => c.Id == campaign.Id);

        Assert.Equal(CampaignStatus.Draft, stored.Status);
        Assert.Equal(new DateTime(2026, 10, 1, 11, 0, 0).Ticks, stored.StartsAt.Ticks);
        Assert.Equal(new DateTime(2026, 11, 1, 2, 59, 0).Ticks, stored.EndsAt.Ticks);
    }

    [Fact]
    public async Task Create_ByGestor_ReturnsCreated()
    {
        var response = await Client(Roles.Gestor).PostAsJsonAsync(Route, Request("Campanha do gestor", "2026-10-01T08:00:00", "2026-10-02T08:00:00"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_Scenario2_InvertedPeriod_IsRejected()
    {
        const string name = "Período invertido";

        var response = await Client().PostAsJsonAsync(Route, Request(name, "2026-10-31T08:00:00", "2026-10-01T08:00:00"));

        await AssertBusinessRuleAsync(response, "CAMPAIGN_INVALID_PERIOD", "A data de início não pode ser posterior à data de fim");
        await AssertNotStoredAsync(name);
    }

    [Fact]
    public async Task Create_Scenario4_StartBeforeToday_IsRejected()
    {
        const string name = "Início no passado";

        var response = await Client().PostAsJsonAsync(Route, Request(name, "2026-09-20T08:00:00", "2026-10-31T23:59:00"));

        await AssertBusinessRuleAsync(response, "CAMPAIGN_START_IN_PAST", "A data de início não pode ser anterior à data atual");
        await AssertNotStoredAsync(name);
    }

    [Fact]
    public async Task Create_StartEarlierToday_InNetworkTimeZone_IsRejected()
    {
        // 11:00 em Brasília é anterior ao "agora" (12:00 em Brasília), embora 11:00 UTC não seja.
        var response = await Client().PostAsJsonAsync(Route, Request("Mais cedo hoje", "2026-09-28T11:00:00", "2026-10-31T23:59:00"));

        await AssertBusinessRuleAsync(response, "CAMPAIGN_START_IN_PAST", "A data de início não pode ser anterior à data atual");
    }

    [Theory]
    [InlineData("{ \"networkId\": \"0199e3a0-0000-7000-8000-000000000001\", \"startsAt\": \"2026-10-01T08:00:00\", \"endsAt\": \"2026-10-31T23:59:00\" }", "Name", "Informe o nome da campanha.")]
    [InlineData("{ \"networkId\": \"0199e3a0-0000-7000-8000-000000000001\", \"name\": \"Sem datas\" }", "StartsAt", "Informe a data e hora de início.")]
    [InlineData("{ \"networkId\": \"0199e3a0-0000-7000-8000-000000000001\", \"name\": \"Com fuso\", \"startsAt\": \"2026-10-01T08:00:00Z\", \"endsAt\": \"2026-10-31T23:59:00\" }", "StartsAt", "Informe data e hora sem fuso; elas são interpretadas no fuso da rede.")]
    [InlineData("{ \"networkId\": \"0199e3a0-0000-7000-8000-000000000001\", \"name\": \"Com offset\", \"startsAt\": \"2026-10-01T08:00:00\", \"endsAt\": \"2026-10-31T23:59:00-03:00\" }", "EndsAt", "Informe data e hora sem fuso; elas são interpretadas no fuso da rede.")]
    [InlineData("{ \"name\": \"Sem rede\", \"startsAt\": \"2026-10-01T08:00:00\", \"endsAt\": \"2026-10-31T23:59:00\" }", "NetworkId", "Informe a rede da campanha.")]
    [InlineData("{ \"networkId\": \"00000000-0000-0000-0000-000000000000\", \"name\": \"Rede vazia\", \"startsAt\": \"2026-10-01T08:00:00\", \"endsAt\": \"2026-10-31T23:59:00\" }", "NetworkId", "Informe a rede da campanha.")]
    public async Task Create_WithInvalidContract_ReturnsValidationProblem(string json, string field, string message)
    {
        var response = await Client().PostAsync(Route, new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await TestAuth.ReadProblemAsync(response);

        Assert.Equal("VALIDATION_ERROR", problem.GetProperty("code").GetString());
        Assert.Contains(message, problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(error => error.GetString()));
    }

    [Fact]
    public async Task Create_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(Route, Request("Sem token", "2026-10-01T08:00:00", "2026-10-31T23:59:00"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_ForNetworkOutsideUserScope_ReturnsScopeForbidden()
    {
        var response = await Client().PostAsJsonAsync(
            Route, Request("Outra rede", "2026-10-01T08:00:00", "2026-10-31T23:59:00", TestAuth.OtherNetworkId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SCOPE_FORBIDDEN", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_ForNetworkInScopeButNotRegistered_ReturnsNetworkUnavailable()
    {
        var client = Client(networkId: TestAuth.OtherNetworkId);

        var response = await client.PostAsJsonAsync(
            Route, Request("Rede inexistente", "2026-10-01T08:00:00", "2026-10-31T23:59:00", TestAuth.OtherNetworkId));

        await AssertBusinessRuleAsync(response, "NETWORK_UNAVAILABLE", "A rede informada não existe ou está inativa.");
    }

    // Token com a hora real: o relógio fixo da Api não vale para a validação do JWT.
    private HttpClient Client(string role = Roles.Analista, Guid? networkId = null) =>
        _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, role, networkId));

    private static object Request(string name, string startsAt, string endsAt, Guid? networkId = null) => new
    {
        networkId = networkId ?? DemoDataSeeder.NetworkId,
        name,
        startsAt,
        endsAt
    };

    private static async Task AssertBusinessRuleAsync(HttpResponseMessage response, string code, string message)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = await TestAuth.ReadProblemAsync(response);

        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal(message, problem.GetProperty("detail").GetString());
    }

    private async Task AssertNotStoredAsync(string name)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await context.Campaigns.AnyAsync(campaign => campaign.Name == name));
    }
}
