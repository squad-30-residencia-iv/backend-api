using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Infrastructure.Persistence;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;

namespace VoucherCampaigns.IntegrationTests.Campaigns;

/// <summary>Público elegível (US-01.5) e edição dos dados básicos em rascunho (US-01.7).</summary>
public class CampaignDraftEditingEndpointTests : IClassFixture<ApiFactory>
{
    private const string Route = "/api/v1/campaigns";

    // 28/09/2026, meio-dia em Brasília.
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(-3));
    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    private readonly ApiFactory _factory;

    public CampaignDraftEditingEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task PutAudience_Scenario1_AllCustomers_IsStored()
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsJsonAsync(AudienceRoute(campaign), new { audienceType = "AllCustomers" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("AllCustomers", (await response.Content.ReadFromJsonAsync<CampaignResponse>())!.AudienceType);

        var detail = await Client().GetFromJsonAsync<CampaignResponse>($"{Route}/{campaign}");
        Assert.Equal("AllCustomers", detail!.AudienceType);

        await using var scope = _factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Campaigns.SingleAsync(c => c.Id == campaign);
        Assert.Equal(CampaignAudienceType.AllCustomers, stored.AudienceType);
    }

    [Fact]
    public async Task PutAudience_IgnoresCase()
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsJsonAsync(AudienceRoute(campaign), new { audienceType = "allcustomers" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("AllCustomers", (await response.Content.ReadFromJsonAsync<CampaignResponse>())!.AudienceType);
    }

    [Fact]
    public async Task Post_ReturnsAudienceNotDefined()
    {
        var response = await Client().PostAsJsonAsync(Route, CampaignRequest(DemoDataSeeder.NetworkId));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, body.GetProperty("audienceType").ValueKind);
    }

    [Fact]
    public async Task PutAudience_Scenario2_Restricted_IsRejectedAndKeepsPreviousAudience()
    {
        var campaign = await CreateCampaignAsync();
        await Client().PutAsJsonAsync(AudienceRoute(campaign), new { audienceType = "AllCustomers" });

        var response = await Client().PutAsJsonAsync(AudienceRoute(campaign), new { audienceType = "Restricted" });

        await AssertBusinessRuleAsync(
            response,
            "CAMPAIGN_AUDIENCE_RESTRICTED_UNAVAILABLE",
            "O público restrito ainda não está disponível: os critérios de elegibilidade serão configurados no EP-06.");

        var detail = await Client().GetFromJsonAsync<CampaignResponse>($"{Route}/{campaign}");
        Assert.Equal("AllCustomers", detail!.AudienceType);
    }

    [Theory]
    [InlineData("""{}""", CampaignContract.AudienceTypeRequired)]
    [InlineData("""{"audienceType":""}""", CampaignContract.AudienceTypeRequired)]
    [InlineData("""{"audienceType":"0"}""", CampaignContract.AudienceTypeInvalid)]
    [InlineData("""{"audienceType":"Todos"}""", CampaignContract.AudienceTypeInvalid)]
    public async Task PutAudience_WithInvalidContract_ReturnsValidationProblem(string json, string message)
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsync(AudienceRoute(campaign), Json(json));

        await AssertValidationAsync(response, "AudienceType", message);
    }

    [Fact]
    public async Task Put_ReplacesNameDescriptionAndPeriod()
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsJsonAsync(
            $"{Route}/{campaign}",
            UpdateRequest("Volta às Aulas 2026", "Material escolar", "2026-10-05T09:00:00", "2026-11-15T23:59:00"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await Client().GetFromJsonAsync<CampaignResponse>($"{Route}/{campaign}");
        Assert.Equal("Volta às Aulas 2026", detail!.Name);
        Assert.Equal("Material escolar", detail.Description);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 9, 0, 0, Brasilia), detail.StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 23, 59, 0, Brasilia), detail.EndsAt);
        Assert.Equal(Brasilia, detail.StartsAt.Offset);
    }

    [Fact]
    public async Task Put_WithoutDescription_ClearsIt()
    {
        var campaign = await CreateCampaignAsync(description: "Descrição antiga");

        var response = await Client().PutAsJsonAsync($"{Route}/{campaign}", UpdateRequest("Volta às Aulas"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await response.Content.ReadFromJsonAsync<CampaignResponse>())!.Description);
    }

    // Cenário 1 da US-01.7: em rascunho, o benefício é editado pelo PUT /items da US-01.2.
    [Fact]
    public async Task Scenario1_BenefitChangedFrom15To20PercentInDraft()
    {
        var campaign = await CreateCampaignAsync();
        await Client().PutAsJsonAsync($"{Route}/{campaign}/items", Items(15));

        var response = await Client().PutAsJsonAsync($"{Route}/{campaign}/items", Items(20));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(20m, Assert.Single((await response.Content.ReadFromJsonAsync<CampaignResponse>())!.Items).BenefitValue);
    }

    [Theory]
    [InlineData("2026-11-01T08:00:00", "2026-10-31T23:59:00")]
    [InlineData("2026-10-31T23:59:00", "2026-10-31T23:59:00")]
    public async Task Put_StartNotBeforeEnd_ReturnsInvalidPeriod(string startsAt, string endsAt)
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsJsonAsync($"{Route}/{campaign}", UpdateRequest("X", startsAt: startsAt, endsAt: endsAt));

        await AssertBusinessRuleAsync(response, "CAMPAIGN_INVALID_PERIOD", "A data de início não pode ser posterior à data de fim");
    }

    [Fact]
    public async Task Put_NewStartInPast_ReturnsStartInPast()
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsJsonAsync($"{Route}/{campaign}", UpdateRequest("X", startsAt: "2026-09-01T08:00:00"));

        await AssertBusinessRuleAsync(response, "CAMPAIGN_START_IN_PAST", "A data de início não pode ser anterior à data atual");
    }

    [Fact]
    public async Task Put_UnchangedStartAlreadyPassed_OnlyNameChanged_ReturnsOk()
    {
        var campaign = await CreateCampaignAsync();
        _factory.Clock.UtcNow = new DateTimeOffset(2026, 10, 2, 12, 0, 0, Brasilia);

        var response = await Client().PutAsJsonAsync($"{Route}/{campaign}", UpdateRequest("Nome corrigido"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Nome corrigido", (await response.Content.ReadFromJsonAsync<CampaignResponse>())!.Name);
    }

    [Fact]
    public async Task Put_UnchangedStartPassedAndEndMovedToPast_ReturnsEndInPast()
    {
        var campaign = await CreateCampaignAsync();
        _factory.Clock.UtcNow = new DateTimeOffset(2026, 10, 2, 12, 0, 0, Brasilia);

        var response = await Client().PutAsJsonAsync($"{Route}/{campaign}", UpdateRequest("X", endsAt: "2026-10-01T23:00:00"));

        await AssertBusinessRuleAsync(response, "CAMPAIGN_END_IN_PAST", "A data de fim não pode ser anterior à data atual");
    }

    [Theory]
    [InlineData("""{"startsAt":"2026-10-01T08:00:00","endsAt":"2026-10-31T23:59:00"}""", "Name", CampaignContract.NameRequired)]
    [InlineData("""{"name":"X","startsAt":"2026-10-01T08:00:00"}""", "EndsAt", CampaignContract.EndsAtRequired)]
    [InlineData("""{"name":"X","endsAt":"2026-10-31T23:59:00"}""", "StartsAt", CampaignContract.StartsAtRequired)]
    [InlineData(
        """{"name":"X","startsAt":"2026-10-01T08:00:00-03:00","endsAt":"2026-10-31T23:59:00"}""",
        "StartsAt",
        "Informe data e hora sem fuso; elas são interpretadas no fuso da rede.")]
    public async Task Put_WithInvalidContract_ReturnsValidationProblem(string json, string field, string message)
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsync($"{Route}/{campaign}", Json(json));

        await AssertValidationAsync(response, field, message);
    }

    [Fact]
    public async Task Put_WithTooLongNameOrDescription_ReturnsValidationProblem()
    {
        var campaign = await CreateCampaignAsync();

        var name = await Client().PutAsJsonAsync($"{Route}/{campaign}", UpdateRequest(new string('a', 151)));
        var description = await Client().PutAsJsonAsync($"{Route}/{campaign}", UpdateRequest("X", new string('a', 501)));

        await AssertValidationAsync(name, "Name", CampaignContract.NameTooLong);
        await AssertValidationAsync(description, "Description", CampaignContract.DescriptionTooLong);
    }

    [Fact]
    public async Task Put_KeepsItemsQuantitiesAndAudience()
    {
        var campaign = await CreateCampaignAsync();
        await Client().PutAsJsonAsync($"{Route}/{campaign}/items", Items(10));
        await Client().PutAsJsonAsync($"{Route}/{campaign}/quantities", new { totalVoucherLimit = 100, perCustomerVoucherLimit = 2 });
        await Client().PutAsJsonAsync(AudienceRoute(campaign), new { audienceType = "AllCustomers" });

        var response = await Client().PutAsJsonAsync($"{Route}/{campaign}", UpdateRequest("Outro nome"));
        var body = (await response.Content.ReadFromJsonAsync<CampaignResponse>())!;

        Assert.Single(body.Items);
        Assert.Equal(100, body.TotalVoucherLimit);
        Assert.Equal(2, body.PerCustomerVoucherLimit);
        Assert.Equal("AllCustomers", body.AudienceType);
    }

    [Theory]
    [InlineData(CampaignStatus.PendingApproval)]
    [InlineData(CampaignStatus.Active)]
    public async Task Put_OutsideDraft_ReturnsNotDraft(CampaignStatus status)
    {
        var campaign = await CreateCampaignAsync();
        await SetStatusAsync(campaign, status);

        await AssertBusinessRuleAsync(
            await Client().PutAsync(ResourceRoute(campaign, "audience"), Json(ValidBody("audience"))),
            "CAMPAIGN_NOT_DRAFT",
            "Somente campanhas em rascunho podem ter a configuração alterada.");
        await AssertBusinessRuleAsync(
            await Client().PutAsync(ResourceRoute(campaign, ""), Json(ValidBody(""))),
            "CAMPAIGN_NOT_DRAFT");

        var detail = await Client().GetFromJsonAsync<CampaignResponse>($"{Route}/{campaign}");
        Assert.Equal("Volta às Aulas", detail!.Name);
        Assert.Null(detail.AudienceType);
    }

    [Theory]
    [InlineData("audience")]
    [InlineData("")]
    public async Task Put_ForCampaignOfAnotherNetwork_ReturnsScopeForbidden(string resource)
    {
        var otherNetwork = await AddNetworkAsync();
        var foreign = await CreateCampaignAsync(Client(networkId: otherNetwork), otherNetwork);

        var response = await Client().PutAsync(ResourceRoute(foreign, resource), Json(ValidBody(resource)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SCOPE_FORBIDDEN", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("audience")]
    [InlineData("")]
    public async Task Put_WithUnknownCampaign_ReturnsNotFound(string resource)
    {
        var response = await Client().PutAsync(ResourceRoute(Guid.NewGuid(), resource), Json(ValidBody(resource)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NOT_FOUND", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("audience")]
    [InlineData("")]
    public async Task Put_WithoutTokenOrAsPlatformAdmin_IsRejected(string resource)
    {
        var campaign = await CreateCampaignAsync();

        var anonymous = await _factory.CreateClient().PutAsync(ResourceRoute(campaign, resource), Json(ValidBody(resource)));
        var admin = await Client(Roles.Administrador).PutAsync(ResourceRoute(campaign, resource), Json(ValidBody(resource)));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
        Assert.Equal("AUTH_FORBIDDEN_ROLE", (await TestAuth.ReadProblemAsync(admin)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("audience")]
    [InlineData("")]
    public async Task Put_ByGestor_ReturnsOk(string resource)
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client(Roles.Gestor).PutAsync(ResourceRoute(campaign, resource), Json(ValidBody(resource)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Token com a hora real: o relógio fixo da Api não vale para a validação do JWT.
    private HttpClient Client(string role = Roles.Analista, Guid? networkId = null) =>
        _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, role, networkId));

    private static string AudienceRoute(Guid campaign) => $"{Route}/{campaign}/audience";

    private static string ResourceRoute(Guid campaign, string resource) =>
        resource == string.Empty ? $"{Route}/{campaign}" : $"{Route}/{campaign}/{resource}";

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static string ValidBody(string resource) => resource == "audience"
        ? """{"audienceType":"AllCustomers"}"""
        : """{"name":"Volta às Aulas 2026","startsAt":"2026-10-01T08:00:00","endsAt":"2026-10-31T23:59:00"}""";

    private static object Items(decimal percentage) =>
        new { items = new[] { new { targetType = "All", benefitType = "Percentage", benefitValue = percentage } } };

    private static object UpdateRequest(
        string name,
        string? description = null,
        string startsAt = "2026-10-01T08:00:00",
        string endsAt = "2026-10-31T23:59:00") =>
        new { name, description, startsAt, endsAt };

    private static object CampaignRequest(Guid networkId, string? description = null) => new
    {
        networkId,
        name = "Volta às Aulas",
        description,
        startsAt = "2026-10-01T08:00:00",
        endsAt = "2026-10-31T23:59:00"
    };

    private Task<Guid> CreateCampaignAsync(string? description = null) =>
        CreateCampaignAsync(Client(), DemoDataSeeder.NetworkId, description);

    private static async Task<Guid> CreateCampaignAsync(HttpClient client, Guid networkId, string? description = null)
    {
        var response = await client.PostAsJsonAsync(Route, CampaignRequest(networkId, description));
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CampaignResponse>())!.Id;
    }

    private async Task<Guid> AddNetworkAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var network = new Network { Name = $"Rede {Guid.NewGuid():N}", TimeZone = "America/Sao_Paulo" };
        context.Networks.Add(network);
        await context.SaveChangesAsync();

        return network.Id;
    }

    /// <summary>Grava o status direto no banco, pois submissão e aprovação ainda não existem.</summary>
    private async Task SetStatusAsync(Guid campaign, CampaignStatus status)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await context.Campaigns.Where(c => c.Id == campaign).ExecuteUpdateAsync(setters => setters.SetProperty(c => c.Status, status));
    }

    private static async Task AssertBusinessRuleAsync(HttpResponseMessage response, string code, string? message = null)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = await TestAuth.ReadProblemAsync(response);

        Assert.Equal(code, problem.GetProperty("code").GetString());

        if (message is not null)
        {
            Assert.Equal(message, problem.GetProperty("detail").GetString());
        }
    }

    private static async Task AssertValidationAsync(HttpResponseMessage response, string field, string message)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await TestAuth.ReadProblemAsync(response);

        Assert.Equal("VALIDATION_ERROR", problem.GetProperty("code").GetString());
        Assert.Contains(message, problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(error => error.GetString()));
    }
}
