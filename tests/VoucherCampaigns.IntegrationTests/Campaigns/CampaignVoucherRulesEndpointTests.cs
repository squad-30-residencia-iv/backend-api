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

/// <summary>Validade do voucher e restrições de uso (US-01.12). Campanha de 01/10/2026 08:00 a 31/10/2026 23:59.</summary>
public class CampaignVoucherRulesEndpointTests : IClassFixture<ApiFactory>
{
    private const string Route = "/api/v1/campaigns";

    // 28/09/2026, meio-dia em Brasília.
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(-3));
    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    private readonly ApiFactory _factory;

    public CampaignVoucherRulesEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task Post_ReturnsVoucherRulesNotDefined()
    {
        var response = await Client().PostAsJsonAsync(Route, CampaignRequest(DemoDataSeeder.NetworkId));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, body.GetProperty("voucherValidity").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("minPurchaseAmount").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("maxPurchaseAmount").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("maxUsesPerVoucher").ValueKind);
    }

    // Exemplos da US-04.3.
    [Fact]
    public async Task PutValidity_Relative30Days_IsStored()
    {
        var campaign = await CreateCampaignAsync();

        var validity = (await PutValidityAsync(campaign, new { type = "Relative", amount = 30, unit = "Days" })).VoucherValidity!;

        Assert.Equal("Relative", validity.Type);
        Assert.Equal(30, validity.Amount);
        Assert.Equal("Days", validity.Unit);
        Assert.Null(validity.Cap);
        Assert.Null(validity.ValidUntil);
    }

    [Fact]
    public async Task PutValidity_FixedDate_IsStoredWithNetworkOffset()
    {
        var campaign = await CreateCampaignAsync();

        await PutValidityAsync(campaign, new { type = "FixedDate", validUntil = "2026-11-15T23:59:00" });

        var validity = (await Client().GetFromJsonAsync<CampaignResponse>($"{Route}/{campaign}"))!.VoucherValidity!;
        Assert.Equal("FixedDate", validity.Type);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 23, 59, 0, Brasilia), validity.ValidUntil);
        Assert.Equal(Brasilia, validity.ValidUntil!.Value.Offset);
        Assert.Null(validity.Amount);
        Assert.Null(validity.Unit);

        await using var scope = _factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Campaigns.SingleAsync(c => c.Id == campaign);
        Assert.Equal(new DateTime(2026, 11, 16, 2, 59, 0), stored.VoucherValidUntil);
    }

    [Fact]
    public async Task PutValidity_60DaysWithCapOnCampaignEnd_IsStored()
    {
        var campaign = await CreateCampaignAsync();

        var validity = (await PutValidityAsync(
            campaign, new { type = "Relative", amount = 60, unit = "Days", cap = "2026-10-31T23:59:00" })).VoucherValidity!;

        Assert.Equal(60, validity.Amount);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 23, 59, 0, Brasilia), validity.Cap);
    }

    [Fact]
    public async Task PutValidity_48Hours_IsStored()
    {
        var campaign = await CreateCampaignAsync();

        var validity = (await PutValidityAsync(campaign, new { type = "relative", amount = 48, unit = "hours" })).VoucherValidity!;

        Assert.Equal(48, validity.Amount);
        Assert.Equal("Hours", validity.Unit);
    }

    [Fact]
    public async Task PutValidity_RelativeToFixed_ClearsAmountUnitAndCap()
    {
        var campaign = await CreateCampaignAsync();
        await PutValidityAsync(campaign, new { type = "Relative", amount = 60, unit = "Days", cap = "2026-10-31T23:59:00" });

        var validity = (await PutValidityAsync(campaign, new { type = "FixedDate", validUntil = "2026-10-31T23:59:00" })).VoucherValidity!;

        Assert.Equal("FixedDate", validity.Type);
        Assert.Null(validity.Amount);
        Assert.Null(validity.Unit);
        Assert.Null(validity.Cap);
    }

    [Fact]
    public async Task PutValidity_FixedDateBeforeCampaignEnd_IsRejected()
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsJsonAsync(
            ValidityRoute(campaign), new { type = "FixedDate", validUntil = "2026-10-31T23:58:00" });

        await AssertBusinessRuleAsync(
            response, "CAMPAIGN_VOUCHER_VALID_UNTIL_BEFORE_END", "A validade fixa do voucher não pode ser anterior ao fim da campanha");
    }

    [Theory]
    [InlineData("2026-10-01T08:00:00", HttpStatusCode.UnprocessableEntity)]
    [InlineData("2026-09-30T23:59:00", HttpStatusCode.UnprocessableEntity)]
    [InlineData("2026-10-15T12:00:00", HttpStatusCode.OK)]
    public async Task PutValidity_CapMustBeAfterCampaignStart(string cap, HttpStatusCode expected)
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsJsonAsync(ValidityRoute(campaign), new { type = "Relative", amount = 30, unit = "Days", cap });

        Assert.Equal(expected, response.StatusCode);

        if (expected == HttpStatusCode.UnprocessableEntity)
        {
            await AssertBusinessRuleAsync(
                response, "CAMPAIGN_VOUCHER_VALIDITY_CAP_BEFORE_START", "O teto da validade do voucher deve ser posterior ao início da campanha");
        }
    }

    [Fact]
    public async Task PutCampaign_EndAfterFixedValidity_IsRejectedAndKeepsPeriod()
    {
        var campaign = await CreateCampaignAsync();
        await PutValidityAsync(campaign, new { type = "FixedDate", validUntil = "2026-11-15T23:59:00" });

        var response = await Client().PutAsJsonAsync(
            $"{Route}/{campaign}",
            new { name = "Volta às Aulas", startsAt = "2026-10-01T08:00:00", endsAt = "2026-11-30T23:59:00" });

        await AssertBusinessRuleAsync(response, "CAMPAIGN_VOUCHER_VALID_UNTIL_BEFORE_END");

        var detail = await Client().GetFromJsonAsync<CampaignResponse>($"{Route}/{campaign}");
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 23, 59, 0, Brasilia), detail!.EndsAt);
    }

    [Fact]
    public async Task PutCampaign_StartAfterCap_IsRejected()
    {
        var campaign = await CreateCampaignAsync();
        await PutValidityAsync(campaign, new { type = "Relative", amount = 30, unit = "Days", cap = "2026-10-10T23:59:00" });

        var response = await Client().PutAsJsonAsync(
            $"{Route}/{campaign}",
            new { name = "Volta às Aulas", startsAt = "2026-10-11T08:00:00", endsAt = "2026-10-31T23:59:00" });

        await AssertBusinessRuleAsync(response, "CAMPAIGN_VOUCHER_VALIDITY_CAP_BEFORE_START");
    }

    [Theory]
    [InlineData("""{}""", "Type", CampaignContract.ValidityTypeRequired)]
    [InlineData("""{"type":"Mensal"}""", "Type", CampaignContract.ValidityTypeInvalid)]
    [InlineData("""{"type":"1"}""", "Type", CampaignContract.ValidityTypeInvalid)]
    [InlineData("""{"type":"Relative","unit":"Days"}""", "Amount", CampaignContract.ValidityAmountRequired)]
    [InlineData("""{"type":"Relative","amount":0,"unit":"Days"}""", "Amount", CampaignContract.ValidityAmountRange)]
    [InlineData("""{"type":"Relative","amount":3651,"unit":"Days"}""", "Amount", CampaignContract.ValidityAmountRange)]
    [InlineData("""{"type":"Relative","amount":87601,"unit":"Hours"}""", "Amount", CampaignContract.ValidityAmountRange)]
    [InlineData("""{"type":"Relative","amount":30}""", "Unit", CampaignContract.ValidityUnitRequired)]
    [InlineData("""{"type":"Relative","amount":30,"unit":"Weeks"}""", "Unit", CampaignContract.ValidityUnitInvalid)]
    [InlineData("""{"type":"Relative","amount":30,"unit":"Days","validUntil":"2026-11-15T23:59:00"}""", "ValidUntil", CampaignContract.ValidityRelativeWithDate)]
    [InlineData("""{"type":"FixedDate"}""", "ValidUntil", CampaignContract.ValidUntilRequired)]
    [InlineData("""{"type":"FixedDate","validUntil":"2026-11-15T23:59:00","amount":30}""", "Amount", CampaignContract.ValidityFixedWithRelative)]
    [InlineData("""{"type":"FixedDate","validUntil":"2026-11-15T23:59:00","cap":"2026-11-15T23:59:00"}""", "Cap", CampaignContract.ValidityFixedWithRelative)]
    public async Task PutValidity_WithInvalidContract_ReturnsValidationProblem(string json, string field, string message)
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsync(ValidityRoute(campaign), Json(json));

        await AssertValidationAsync(response, field, message);
    }

    [Fact]
    public async Task PutValidity_DateWithOffset_ReturnsValidationProblem()
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsync(
            ValidityRoute(campaign), Json("""{"type":"FixedDate","validUntil":"2026-11-15T23:59:00Z"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await TestAuth.ReadProblemAsync(response)).GetProperty("errors").TryGetProperty("ValidUntil", out _));
    }

    [Fact]
    public async Task PutRestrictions_MinMaxAndOneUse_IsStored()
    {
        var campaign = await CreateCampaignAsync();

        var body = await PutRestrictionsAsync(campaign, new { minPurchaseAmount = 50.00m, maxPurchaseAmount = 500.00m, maxUsesPerVoucher = 1 });

        Assert.Equal(50.00m, body.MinPurchaseAmount);
        Assert.Equal(500.00m, body.MaxPurchaseAmount);
        Assert.Equal(1, body.MaxUsesPerVoucher);

        var detail = await Client().GetFromJsonAsync<CampaignResponse>($"{Route}/{campaign}");
        Assert.Equal(500.00m, detail!.MaxPurchaseAmount);
    }

    [Fact]
    public async Task PutRestrictions_OnlyMax_ClearsPreviousMin()
    {
        var campaign = await CreateCampaignAsync();
        await PutRestrictionsAsync(campaign, new { minPurchaseAmount = 50m, maxUsesPerVoucher = 1 });

        var body = await PutRestrictionsAsync(campaign, new { maxPurchaseAmount = 300m, maxUsesPerVoucher = 3 });

        Assert.Null(body.MinPurchaseAmount);
        Assert.Equal(300m, body.MaxPurchaseAmount);
        Assert.Equal(3, body.MaxUsesPerVoucher);
    }

    [Fact]
    public async Task PutRestrictions_MinAboveMax_IsRejected()
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsJsonAsync(
            RestrictionsRoute(campaign), new { minPurchaseAmount = 600m, maxPurchaseAmount = 500m, maxUsesPerVoucher = 1 });

        await AssertBusinessRuleAsync(
            response, "CAMPAIGN_PURCHASE_RANGE_INVALID", "O valor mínimo da compra não pode ser maior que o valor máximo");
    }

    [Theory]
    [InlineData("""{}""", "MaxUsesPerVoucher", CampaignContract.MaxUsesRequired)]
    [InlineData("""{"maxUsesPerVoucher":0}""", "MaxUsesPerVoucher", CampaignContract.MaxUsesRange)]
    [InlineData("""{"maxUsesPerVoucher":101}""", "MaxUsesPerVoucher", CampaignContract.MaxUsesRange)]
    [InlineData("""{"minPurchaseAmount":0,"maxUsesPerVoucher":1}""", "MinPurchaseAmount", CampaignContract.PurchaseAmountPositive)]
    [InlineData("""{"maxPurchaseAmount":-10,"maxUsesPerVoucher":1}""", "MaxPurchaseAmount", CampaignContract.PurchaseAmountPositive)]
    [InlineData("""{"minPurchaseAmount":10.555,"maxUsesPerVoucher":1}""", "MinPurchaseAmount", CampaignContract.PurchaseAmountScale)]
    [InlineData("""{"maxPurchaseAmount":100000000,"maxUsesPerVoucher":1}""", "MaxPurchaseAmount", CampaignContract.PurchaseAmountTooLarge)]
    public async Task PutRestrictions_WithInvalidContract_ReturnsValidationProblem(string json, string field, string message)
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsync(RestrictionsRoute(campaign), Json(json));

        await AssertValidationAsync(response, field, message);
    }

    [Theory]
    [InlineData("voucher-validity", CampaignStatus.PendingApproval)]
    [InlineData("usage-restrictions", CampaignStatus.Active)]
    public async Task Put_OutsideDraft_IsRejected(string resource, CampaignStatus status)
    {
        var campaign = await CreateCampaignAsync();
        await SetStatusAsync(campaign, status);

        var response = await Client().PutAsync($"{Route}/{campaign}/{resource}", Json(ValidBody(resource)));

        await AssertBusinessRuleAsync(response, "CAMPAIGN_NOT_DRAFT");
    }

    [Theory]
    [InlineData("voucher-validity")]
    [InlineData("usage-restrictions")]
    public async Task Put_CampaignFromAnotherNetwork_ReturnsForbidden(string resource)
    {
        var otherNetwork = await AddNetworkAsync();
        var foreign = await CreateCampaignAsync(Client(networkId: otherNetwork), otherNetwork);

        var response = await Client().PutAsync($"{Route}/{foreign}/{resource}", Json(ValidBody(resource)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SCOPE_FORBIDDEN", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("voucher-validity")]
    [InlineData("usage-restrictions")]
    public async Task Put_UnknownCampaign_ReturnsNotFound(string resource)
    {
        var response = await Client().PutAsync($"{Route}/{Guid.CreateVersion7()}/{resource}", Json(ValidBody(resource)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("voucher-validity")]
    [InlineData("usage-restrictions")]
    public async Task Put_WithoutTokenOrAsPlatformAdmin_IsRejected(string resource)
    {
        var campaign = await CreateCampaignAsync();

        var anonymous = await _factory.CreateClient().PutAsync($"{Route}/{campaign}/{resource}", Json(ValidBody(resource)));
        var admin = await Client(Roles.Administrador).PutAsync($"{Route}/{campaign}/{resource}", Json(ValidBody(resource)));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
    }

    [Theory]
    [InlineData("voucher-validity")]
    [InlineData("usage-restrictions")]
    public async Task Put_ByGestor_ReturnsOk(string resource)
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client(Roles.Gestor).PutAsync($"{Route}/{campaign}/{resource}", Json(ValidBody(resource)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Token com a hora real: o relógio fixo da Api não vale para a validação do JWT.
    private HttpClient Client(string role = Roles.Analista, Guid? networkId = null) =>
        _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, role, networkId));

    private static string ValidityRoute(Guid campaign) => $"{Route}/{campaign}/voucher-validity";

    private static string RestrictionsRoute(Guid campaign) => $"{Route}/{campaign}/usage-restrictions";

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static string ValidBody(string resource) => resource == "voucher-validity"
        ? """{"type":"Relative","amount":30,"unit":"Days"}"""
        : """{"maxUsesPerVoucher":1}""";

    private static object CampaignRequest(Guid networkId) => new
    {
        networkId,
        name = "Volta às Aulas",
        startsAt = "2026-10-01T08:00:00",
        endsAt = "2026-10-31T23:59:00"
    };

    private Task<Guid> CreateCampaignAsync() => CreateCampaignAsync(Client(), DemoDataSeeder.NetworkId);

    private static async Task<Guid> CreateCampaignAsync(HttpClient client, Guid networkId)
    {
        var response = await client.PostAsJsonAsync(Route, CampaignRequest(networkId));
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CampaignResponse>())!.Id;
    }

    private async Task<CampaignResponse> PutValidityAsync(Guid campaign, object body)
    {
        var response = await Client().PutAsJsonAsync(ValidityRoute(campaign), body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CampaignResponse>())!;
    }

    private async Task<CampaignResponse> PutRestrictionsAsync(Guid campaign, object body)
    {
        var response = await Client().PutAsJsonAsync(RestrictionsRoute(campaign), body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CampaignResponse>())!;
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
