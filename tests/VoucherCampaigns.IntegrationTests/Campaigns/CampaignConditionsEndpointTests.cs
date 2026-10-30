using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Infrastructure.Persistence;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;

namespace VoucherCampaigns.IntegrationTests.Campaigns;

/// <summary>Condições de geração do voucher e uso na mesma compra (US-01.4).</summary>
public class CampaignConditionsEndpointTests : IClassFixture<ApiFactory>
{
    private const string Route = "/api/v1/campaigns";

    // 28/09/2026, meio-dia em Brasília.
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(-3));

    private readonly ApiFactory _factory;

    public CampaignConditionsEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task Post_ReturnsNoConditionsAndSamePurchaseDisabled()
    {
        var response = await Client().PostAsJsonAsync(Route, CampaignRequest(DemoDataSeeder.NetworkId));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, body.GetProperty("conditions").GetArrayLength());
        Assert.False(body.GetProperty("allowUseInSamePurchase").GetBoolean());
    }

    [Fact]
    public async Task Scenario1_PurchaseTotal_IsStored()
    {
        var campaign = await CreateCampaignAsync();

        var body = await PutConditionsAsync(campaign, false, new { type = "PurchaseTotal", minAmount = 150.00m });

        var condition = Assert.Single(body.Conditions);
        Assert.Equal("PurchaseTotal", condition.Type);
        Assert.Equal(150.00m, condition.MinAmount);
        Assert.Null(condition.MinQuantity);
        Assert.Null(condition.CategoryId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .CampaignConditions.SingleAsync(c => c.CampaignId == campaign);
        Assert.Equal(ConditionType.PurchaseTotal, stored.Type);
        Assert.Equal(150.00m, stored.MinAmount);
    }

    [Fact]
    public async Task Scenario2_PurchaseTotalAndCategory_AreStoredInOrder()
    {
        var campaign = await CreateCampaignAsync();
        var (categoryId, _) = await AddCatalogAsync();

        await PutConditionsAsync(
            campaign,
            false,
            new { type = "PurchaseTotal", minAmount = 150.00m },
            new { type = "Category", categoryId });

        var detail = await Client().GetFromJsonAsync<CampaignResponse>($"{Route}/{campaign}");
        Assert.Equal(["PurchaseTotal", "Category"], detail!.Conditions.Select(c => c.Type));
        Assert.Equal(categoryId, detail.Conditions[1].CategoryId);
    }

    [Fact]
    public async Task Scenario3_ZeroItemQuantity_IsRejectedWithBacklogMessageAndKeepsList()
    {
        var campaign = await CreateCampaignAsync();
        await PutConditionsAsync(campaign, false, new { type = "PurchaseTotal", minAmount = 150m });

        var response = await Client().PutAsJsonAsync(
            ConditionsRoute(campaign), Conditions(true, new { type = "ItemQuantity", minQuantity = 0 }));

        await AssertBusinessRuleAsync(
            response, "CAMPAIGN_CONDITION_MIN_QUANTITY_NOT_POSITIVE", "A quantidade mínima de itens deve ser maior que zero");

        var detail = await Client().GetFromJsonAsync<CampaignResponse>($"{Route}/{campaign}");
        Assert.Equal("PurchaseTotal", Assert.Single(detail!.Conditions).Type);
        Assert.False(detail.AllowUseInSamePurchase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Scenario4_AllowUseInSamePurchase_IsStored(bool allow)
    {
        var campaign = await CreateCampaignAsync();
        await PutConditionsAsync(campaign, !allow);

        var body = await PutConditionsAsync(campaign, allow, new { type = "ItemQuantity", minQuantity = 3 });

        Assert.Equal(allow, body.AllowUseInSamePurchase);
        Assert.Equal(3, Assert.Single(body.Conditions).MinQuantity);
    }

    [Fact]
    public async Task Put_EmptyList_ReplacesConditionsAndKeepsItems()
    {
        var campaign = await CreateCampaignAsync();
        var (_, productId) = await AddCatalogAsync();
        await Client().PutAsJsonAsync(
            $"{Route}/{campaign}/items",
            new { items = new[] { new { targetType = "All", benefitType = "Percentage", benefitValue = 10 } } });
        await PutConditionsAsync(campaign, false, new { type = "Product", productId });

        var body = await PutConditionsAsync(campaign, true);

        Assert.Empty(body.Conditions);
        Assert.True(body.AllowUseInSamePurchase);
        Assert.Single(body.Items);
    }

    [Fact]
    public async Task Put_DifferentCategories_AreAccepted()
    {
        var campaign = await CreateCampaignAsync();
        var (first, _) = await AddCatalogAsync();
        var (second, _) = await AddCatalogAsync();

        var body = await PutConditionsAsync(
            campaign, false, new { type = "Category", categoryId = first }, new { type = "Category", categoryId = second });

        Assert.Equal(2, body.Conditions.Count);
    }

    [Fact]
    public async Task Put_RepeatedCondition_IsRejected()
    {
        var campaign = await CreateCampaignAsync();
        var (categoryId, _) = await AddCatalogAsync();

        var totals = await Client().PutAsJsonAsync(
            ConditionsRoute(campaign),
            Conditions(false, new { type = "PurchaseTotal", minAmount = 100m }, new { type = "PurchaseTotal", minAmount = 200m }));
        var categories = await Client().PutAsJsonAsync(
            ConditionsRoute(campaign),
            Conditions(false, new { type = "Category", categoryId }, new { type = "Category", categoryId }));

        await AssertBusinessRuleAsync(totals, "CAMPAIGN_CONDITION_DUPLICATED", "A mesma condição foi informada mais de uma vez.");
        await AssertBusinessRuleAsync(categories, "CAMPAIGN_CONDITION_DUPLICATED");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Put_MinAmountNotPositive_IsRejected(decimal minAmount)
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsJsonAsync(
            ConditionsRoute(campaign), Conditions(false, new { type = "PurchaseTotal", minAmount }));

        await AssertBusinessRuleAsync(
            response, "CAMPAIGN_CONDITION_MIN_AMOUNT_NOT_POSITIVE", "O valor mínimo da compra deve ser maior que zero");
    }

    [Theory]
    [InlineData("CustomerPurchaseCount")]
    [InlineData("ProductCombination")]
    [InlineData("PurchasePeriod")]
    public async Task Put_UnavailableType_IsRejected(string type)
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsJsonAsync(ConditionsRoute(campaign), Conditions(false, new { type }));

        await AssertBusinessRuleAsync(
            response, "CAMPAIGN_CONDITION_TYPE_UNAVAILABLE", "Este tipo de condição ainda não está disponível.");
    }

    [Theory]
    [InlineData("Category", false, "CAMPAIGN_CONDITION_CATEGORY_NOT_FOUND", "Categoria informada não encontrada")]
    [InlineData("Category", true, "CAMPAIGN_CONDITION_CATEGORY_INACTIVE", "Categoria informada está inativa")]
    [InlineData("Product", false, "CAMPAIGN_CONDITION_PRODUCT_NOT_FOUND", "Produto informado não encontrado")]
    [InlineData("Product", true, "CAMPAIGN_CONDITION_PRODUCT_INACTIVE", "Produto informado está inativo")]
    public async Task Put_CatalogMissingOrInactive_IsRejectedAndKeepsList(string type, bool exists, string code, string message)
    {
        var campaign = await CreateCampaignAsync();
        await PutConditionsAsync(campaign, false, new { type = "PurchaseTotal", minAmount = 150m });

        var (categoryId, productId) = exists ? await AddCatalogAsync(active: false) : (Guid.CreateVersion7(), Guid.CreateVersion7());
        object condition = type == "Category" ? new { type, categoryId } : new { type, productId };

        var response = await Client().PutAsJsonAsync(ConditionsRoute(campaign), Conditions(true, condition));

        await AssertBusinessRuleAsync(response, code, message);

        var detail = await Client().GetFromJsonAsync<CampaignResponse>($"{Route}/{campaign}");
        Assert.Equal("PurchaseTotal", Assert.Single(detail!.Conditions).Type);
        Assert.False(detail.AllowUseInSamePurchase);
    }

    [Theory]
    [InlineData("""{"allowUseInSamePurchase":false}""", "Conditions", CampaignContract.ConditionsRequired)]
    [InlineData("""{"conditions":[]}""", "AllowUseInSamePurchase", CampaignContract.AllowUseInSamePurchaseRequired)]
    [InlineData("""{"conditions":[{"minAmount":10}],"allowUseInSamePurchase":false}""", "Conditions[0].Type", CampaignContract.ConditionTypeRequired)]
    [InlineData("""{"conditions":[{"type":"Valor"}],"allowUseInSamePurchase":false}""", "Conditions[0].Type", CampaignContract.ConditionTypeInvalid)]
    [InlineData("""{"conditions":[{"type":"0"}],"allowUseInSamePurchase":false}""", "Conditions[0].Type", CampaignContract.ConditionTypeInvalid)]
    [InlineData("""{"conditions":[{"type":"PurchaseTotal"}],"allowUseInSamePurchase":false}""", "Conditions[0].MinAmount", CampaignContract.ConditionMinAmountRequired)]
    [InlineData("""{"conditions":[{"type":"PurchaseTotal","minAmount":10.555}],"allowUseInSamePurchase":false}""", "Conditions[0].MinAmount", CampaignContract.ConditionMinAmountScale)]
    [InlineData("""{"conditions":[{"type":"ItemQuantity"}],"allowUseInSamePurchase":false}""", "Conditions[0].MinQuantity", CampaignContract.ConditionMinQuantityRequired)]
    [InlineData("""{"conditions":[{"type":"Category"}],"allowUseInSamePurchase":false}""", "Conditions[0].CategoryId", CampaignContract.ConditionCategoryRequired)]
    [InlineData("""{"conditions":[{"type":"Product","productId":"00000000-0000-0000-0000-000000000000"}],"allowUseInSamePurchase":false}""", "Conditions[0].ProductId", CampaignContract.ConditionProductRequired)]
    [InlineData("""{"conditions":[{"type":"PurchaseTotal","minAmount":10,"categoryId":"0199e3a0-0000-7000-8000-000000000301"}],"allowUseInSamePurchase":false}""", "Conditions[0].CategoryId", CampaignContract.ConditionUnexpectedField)]
    [InlineData("""{"conditions":[{"type":"PurchaseTotal","minAmount":10},{"type":"Category","categoryId":"0199e3a0-0000-7000-8000-000000000301","minQuantity":2}],"allowUseInSamePurchase":false}""", "Conditions[1].MinQuantity", CampaignContract.ConditionUnexpectedField)]
    public async Task Put_WithInvalidContract_ReturnsValidationProblem(string json, string field, string message)
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsync(ConditionsRoute(campaign), Json(json));

        await AssertValidationAsync(response, field, message);
    }

    [Fact]
    public async Task Put_MoreThan20Conditions_ReturnsValidationProblem()
    {
        var campaign = await CreateCampaignAsync();
        var conditions = Enumerable.Range(1, 21).Select(quantity => new { type = "ItemQuantity", minQuantity = quantity }).ToArray();

        var response = await Client().PutAsJsonAsync(ConditionsRoute(campaign), new { conditions, allowUseInSamePurchase = false });

        await AssertValidationAsync(response, "Conditions", CampaignContract.ConditionsTooMany);
    }

    [Fact]
    public async Task Put_OutsideDraft_IsRejected()
    {
        var campaign = await CreateCampaignAsync();
        await SetStatusAsync(campaign, CampaignStatus.PendingApproval);

        var response = await Client().PutAsync(ConditionsRoute(campaign), Json(ValidBody));

        await AssertBusinessRuleAsync(response, "CAMPAIGN_NOT_DRAFT");
    }

    [Fact]
    public async Task Put_CampaignFromAnotherNetwork_ReturnsForbidden()
    {
        var otherNetwork = await AddNetworkAsync();
        var foreign = await CreateCampaignAsync(Client(networkId: otherNetwork), otherNetwork);

        var response = await Client().PutAsync(ConditionsRoute(foreign), Json(ValidBody));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SCOPE_FORBIDDEN", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Put_UnknownCampaign_ReturnsNotFound()
    {
        var response = await Client().PutAsync(ConditionsRoute(Guid.CreateVersion7()), Json(ValidBody));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_WithoutTokenOrAsPlatformAdmin_IsRejected()
    {
        var campaign = await CreateCampaignAsync();

        var anonymous = await _factory.CreateClient().PutAsync(ConditionsRoute(campaign), Json(ValidBody));
        var admin = await Client(Roles.Administrador).PutAsync(ConditionsRoute(campaign), Json(ValidBody));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
    }

    [Fact]
    public async Task Put_ByGestor_ReturnsOk()
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client(Roles.Gestor).PutAsync(ConditionsRoute(campaign), Json(ValidBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private const string ValidBody = """{"conditions":[{"type":"PurchaseTotal","minAmount":150}],"allowUseInSamePurchase":false}""";

    // Token com a hora real: o relógio fixo da Api não vale para a validação do JWT.
    private HttpClient Client(string role = Roles.Analista, Guid? networkId = null) =>
        _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, role, networkId));

    private static string ConditionsRoute(Guid campaign) => $"{Route}/{campaign}/conditions";

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static object Conditions(bool allowUseInSamePurchase, params object[] conditions) =>
        new { conditions, allowUseInSamePurchase };

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

    private async Task<CampaignResponse> PutConditionsAsync(Guid campaign, bool allowUseInSamePurchase, params object[] conditions)
    {
        var response = await Client().PutAsJsonAsync(ConditionsRoute(campaign), Conditions(allowUseInSamePurchase, conditions));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CampaignResponse>())!;
    }

    /// <summary>Categoria e produto próprios do teste (o catálogo é global).</summary>
    private async Task<(Guid CategoryId, Guid ProductId)> AddCatalogAsync(bool active = true)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var category = new Category { Name = $"Papelaria {Guid.NewGuid():N}", IsActive = active };
        var product = new Product { CategoryId = category.Id, Code = Guid.NewGuid().ToString("N")[..20], Name = "Mochila Escolar G", IsActive = active };

        context.Categories.Add(category);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        return (category.Id, product.Id);
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
