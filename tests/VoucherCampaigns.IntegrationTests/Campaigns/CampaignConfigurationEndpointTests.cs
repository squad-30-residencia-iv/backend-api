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

/// <summary>Alvos e benefícios (US-01.2) e quantidades (US-01.3) da campanha em rascunho.</summary>
public class CampaignConfigurationEndpointTests : IClassFixture<ApiFactory>
{
    private const string Route = "/api/v1/campaigns";

    // 28/09/2026, meio-dia em Brasília.
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(-3));

    private readonly ApiFactory _factory;

    public CampaignConfigurationEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Now;
    }

    [Fact]
    public async Task PutItems_Scenario1_SingleAllTargetWith10Percent()
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsJsonAsync(ItemsRoute(campaign), Items(new { targetType = "All", benefitType = "Percentage", benefitValue = 10 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CampaignResponse>();
        var item = Assert.Single(body!.Items);
        Assert.Equal("All", item.TargetType);
        Assert.Null(item.CategoryId);
        Assert.Equal("Percentage", item.BenefitType);
        Assert.Equal(10m, item.BenefitValue);

        await using var scope = _factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().CampaignItems.SingleAsync(i => i.CampaignId == campaign);
        Assert.Equal(TargetType.All, stored.TargetType);
    }

    [Fact]
    public async Task PutItems_Scenario2_CategoryAndProductWithDifferentBenefits()
    {
        var campaign = await CreateCampaignAsync();
        var (papelaria, mochila) = await AddCatalogAsync();

        var body = await PutItemsAsync(
            campaign,
            new { targetType = "Category", categoryId = papelaria, benefitType = "Percentage", benefitValue = 15 },
            new { targetType = "Product", productId = mochila, benefitType = "FixedAmount", benefitValue = 20.00m });

        Assert.Collection(
            body.Items,
            item =>
            {
                Assert.Equal("Category", item.TargetType);
                Assert.Equal(papelaria, item.CategoryId);
                Assert.Equal(15m, item.BenefitValue);
            },
            item =>
            {
                Assert.Equal("Product", item.TargetType);
                Assert.Equal(mochila, item.ProductId);
                Assert.Equal("FixedAmount", item.BenefitType);
                Assert.Equal(20m, item.BenefitValue);
            });

        var detail = await Client().GetFromJsonAsync<CampaignResponse>($"{Route}/{campaign}");
        Assert.Equal(body.Items, detail!.Items);
    }

    // Esquema do cenário "Validação do valor do benefício" (RN05).
    [Theory]
    [InlineData("Percentage", "0", "CAMPAIGN_BENEFIT_NOT_POSITIVE")]
    [InlineData("Percentage", "101", "CAMPAIGN_BENEFIT_PERCENTAGE_ABOVE_100")]
    [InlineData("Percentage", "100", null)]
    [InlineData("FixedAmount", "0", "CAMPAIGN_BENEFIT_NOT_POSITIVE")]
    [InlineData("FixedAmount", "-5.00", "CAMPAIGN_BENEFIT_NOT_POSITIVE")]
    [InlineData("FixedAmount", "20.00", null)]
    public async Task PutItems_BenefitValue_FollowsRn05(string type, string value, string? code)
    {
        var campaign = await CreateCampaignAsync();
        var json = $$"""{"items":[{"targetType":"All","benefitType":"{{type}}","benefitValue":{{value}}}]}""";

        var response = await Client().PutAsync(ItemsRoute(campaign), Json(json));

        if (code is null)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return;
        }

        await AssertBusinessRuleAsync(response, code);
        await AssertNoItemsAsync(campaign);
    }

    [Fact]
    public async Task PutItems_ReplacesPreviousList()
    {
        var campaign = await CreateCampaignAsync();
        var (papelaria, _) = await AddCatalogAsync();
        await PutItemsAsync(campaign, new { targetType = "All", benefitType = "Percentage", benefitValue = 10 });

        await PutItemsAsync(campaign, new { targetType = "Category", categoryId = papelaria, benefitType = "Percentage", benefitValue = 20 });

        var detail = await Client().GetFromJsonAsync<CampaignResponse>($"{Route}/{campaign}");
        var item = Assert.Single(detail!.Items);
        Assert.Equal("Category", item.TargetType);
        Assert.Equal(20m, item.BenefitValue);
    }

    [Fact]
    public async Task PutItems_OverlappingTargets_AreAllowed()
    {
        var campaign = await CreateCampaignAsync();
        var (papelaria, mochila) = await AddCatalogAsync();

        var body = await PutItemsAsync(
            campaign,
            new { targetType = "All", benefitType = "Percentage", benefitValue = 5 },
            new { targetType = "Category", categoryId = papelaria, benefitType = "Percentage", benefitValue = 15 },
            new { targetType = "Product", productId = mochila, benefitType = "FixedAmount", benefitValue = 20 });

        Assert.Equal(["All", "Category", "Product"], body.Items.Select(item => item.TargetType));
    }

    [Fact]
    public async Task PutItems_SameTargetTwice_IsRejectedAndKeepsPreviousItems()
    {
        var campaign = await CreateCampaignAsync();
        var (papelaria, _) = await AddCatalogAsync();
        await PutItemsAsync(campaign, new { targetType = "All", benefitType = "Percentage", benefitValue = 10 });

        var response = await Client().PutAsJsonAsync(
            ItemsRoute(campaign),
            Items(
                new { targetType = "Category", categoryId = papelaria, benefitType = "Percentage", benefitValue = 15 },
                new { targetType = "Category", categoryId = papelaria, benefitType = "Percentage", benefitValue = 20 }));

        await AssertBusinessRuleAsync(response, "CAMPAIGN_ITEM_DUPLICATED_TARGET");

        var detail = await Client().GetFromJsonAsync<CampaignResponse>($"{Route}/{campaign}");
        Assert.Equal("All", Assert.Single(detail!.Items).TargetType);
    }

    [Fact]
    public async Task PutItems_WithUnknownOrInactiveCatalogTarget_ReturnsBusinessRule()
    {
        var campaign = await CreateCampaignAsync();
        var (inactiveCategory, inactiveProduct) = await AddCatalogAsync(active: false);

        await AssertBusinessRuleAsync(
            await Client().PutAsJsonAsync(ItemsRoute(campaign), Items(new { targetType = "Category", categoryId = Guid.NewGuid(), benefitType = "Percentage", benefitValue = 10 })),
            "CAMPAIGN_ITEM_CATEGORY_NOT_FOUND",
            "Categoria informada não encontrada");
        await AssertBusinessRuleAsync(
            await Client().PutAsJsonAsync(ItemsRoute(campaign), Items(new { targetType = "Category", categoryId = inactiveCategory, benefitType = "Percentage", benefitValue = 10 })),
            "CAMPAIGN_ITEM_CATEGORY_INACTIVE");
        await AssertBusinessRuleAsync(
            await Client().PutAsJsonAsync(ItemsRoute(campaign), Items(new { targetType = "Product", productId = Guid.NewGuid(), benefitType = "Percentage", benefitValue = 10 })),
            "CAMPAIGN_ITEM_PRODUCT_NOT_FOUND",
            "Produto informado não encontrado");
        await AssertBusinessRuleAsync(
            await Client().PutAsJsonAsync(ItemsRoute(campaign), Items(new { targetType = "Product", productId = inactiveProduct, benefitType = "Percentage", benefitValue = 10 })),
            "CAMPAIGN_ITEM_PRODUCT_INACTIVE");
    }

    [Theory]
    [InlineData("""{"items":[]}""", "Items", CampaignContract.ItemsRequired)]
    [InlineData("""{}""", "Items", CampaignContract.ItemsRequired)]
    [InlineData("""{"items":[{"benefitType":"Percentage","benefitValue":10}]}""", "Items[0].TargetType", CampaignContract.TargetTypeRequired)]
    [InlineData("""{"items":[{"targetType":"All","benefitValue":10}]}""", "Items[0].BenefitType", CampaignContract.BenefitTypeRequired)]
    [InlineData("""{"items":[{"targetType":"All","benefitType":"Percentage"}]}""", "Items[0].BenefitValue", CampaignContract.BenefitValueRequired)]
    [InlineData("""{"items":[{"targetType":"Todos","benefitType":"Percentage","benefitValue":10}]}""", "Items[0].TargetType", CampaignContract.TargetTypeInvalid)]
    [InlineData("""{"items":[{"targetType":"Category","benefitType":"Percentage","benefitValue":10}]}""", "Items[0].CategoryId", CampaignContract.TargetCategoryRequired)]
    [InlineData("""{"items":[{"targetType":"All","benefitType":"Percentage","benefitValue":10.555}]}""", "Items[0].BenefitValue", CampaignContract.BenefitValueScale)]
    [InlineData("""{"items":[{"targetType":"All","benefitType":"Percentage","benefitValue":10},{"targetType":"Category","categoryId":"0199e3a0-0000-7000-8000-000000000301","benefitType":"Percentage","benefitValue":10,"voucherLimit":0}]}""", "Items[1].VoucherLimit", CampaignContract.ItemVoucherLimitPositive)]
    public async Task PutItems_WithInvalidContract_ReturnsValidationProblem(string json, string field, string message)
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsync(ItemsRoute(campaign), Json(json));

        await AssertValidationAsync(response, field, message);
    }

    [Fact]
    public async Task PutItems_WithMoreThan100Targets_ReturnsValidationProblem()
    {
        var campaign = await CreateCampaignAsync();
        var items = Enumerable.Range(0, 101).Select(_ => new { targetType = "All", benefitType = "Percentage", benefitValue = 10 }).ToArray();

        var response = await Client().PutAsJsonAsync(ItemsRoute(campaign), new { items });

        await AssertValidationAsync(response, "Items", CampaignContract.ItemsTooMany);
    }

    [Theory]
    [InlineData(CampaignStatus.PendingApproval)]
    [InlineData(CampaignStatus.Active)]
    public async Task Put_OutsideDraft_ReturnsNotDraft(CampaignStatus status)
    {
        var campaign = await CreateCampaignAsync();
        await SetStatusAsync(campaign, status);

        await AssertBusinessRuleAsync(
            await Client().PutAsJsonAsync(ItemsRoute(campaign), Items(new { targetType = "All", benefitType = "Percentage", benefitValue = 10 })),
            "CAMPAIGN_NOT_DRAFT",
            "Somente campanhas em rascunho podem ter a configuração alterada.");
        await AssertBusinessRuleAsync(
            await Client().PutAsJsonAsync(QuantitiesRoute(campaign), new { totalVoucherLimit = 10 }),
            "CAMPAIGN_NOT_DRAFT");

        await AssertNoItemsAsync(campaign);
    }

    [Fact]
    public async Task PutQuantities_Scenario1_TotalWithSingleAllTarget()
    {
        var campaign = await CreateCampaignAsync();
        await PutItemsAsync(campaign, new { targetType = "All", benefitType = "Percentage", benefitValue = 10 });

        var body = await PutQuantitiesAsync(campaign, new { totalVoucherLimit = 5000 });

        Assert.Equal(5000, body.TotalVoucherLimit);
        Assert.Equal(5000, body.UndistributedVouchers);
        Assert.Null(body.PerCustomerVoucherLimit);
    }

    [Fact]
    public async Task Quotas_Scenario2_WithinTotal_ShowUndistributedBalance()
    {
        var campaign = await CreateCampaignAsync();
        var (papelaria, mochila) = await AddCatalogAsync();
        await PutQuantitiesAsync(campaign, new { totalVoucherLimit = 5000 });

        var body = await PutItemsAsync(
            campaign,
            new { targetType = "Category", categoryId = papelaria, benefitType = "Percentage", benefitValue = 15, voucherLimit = 3000 },
            new { targetType = "Product", productId = mochila, benefitType = "FixedAmount", benefitValue = 20, voucherLimit = 1500 });

        Assert.Equal([3000, 1500], body.Items.Select(item => item.VoucherLimit));
        Assert.Equal(500, body.UndistributedVouchers);
    }

    [Fact]
    public async Task Quotas_Scenario3_AboveTotal_IsRejectedWithBacklogMessage()
    {
        var campaign = await CreateCampaignAsync();
        var (papelaria, mochila) = await AddCatalogAsync();
        await PutQuantitiesAsync(campaign, new { totalVoucherLimit = 5000 });

        var response = await Client().PutAsJsonAsync(
            ItemsRoute(campaign),
            Items(
                new { targetType = "Category", categoryId = papelaria, benefitType = "Percentage", benefitValue = 15, voucherLimit = 3000 },
                new { targetType = "Product", productId = mochila, benefitType = "FixedAmount", benefitValue = 20, voucherLimit = 2500 }));

        await AssertBusinessRuleAsync(
            response,
            "CAMPAIGN_ITEM_QUOTAS_EXCEED_TOTAL",
            "A soma das quantidades por alvo (5.500) ultrapassa a quantidade total da campanha (5.000)");
        await AssertNoItemsAsync(campaign);
    }

    [Fact]
    public async Task PutQuantities_BelowCurrentQuotas_IsRejected()
    {
        var campaign = await CreateCampaignAsync();
        var (papelaria, mochila) = await AddCatalogAsync();

        // Cotas antes do total são aceitas; o saldo fica nulo até o total existir.
        var items = await PutItemsAsync(
            campaign,
            new { targetType = "Category", categoryId = papelaria, benefitType = "Percentage", benefitValue = 15, voucherLimit = 3000 },
            new { targetType = "Product", productId = mochila, benefitType = "FixedAmount", benefitValue = 20, voucherLimit = 1500 });
        Assert.Null(items.UndistributedVouchers);

        var response = await Client().PutAsJsonAsync(QuantitiesRoute(campaign), new { totalVoucherLimit = 4000 });

        await AssertBusinessRuleAsync(
            response,
            "CAMPAIGN_ITEM_QUOTAS_EXCEED_TOTAL",
            "A soma das quantidades por alvo (4.500) ultrapassa a quantidade total da campanha (4.000)");

        var detail = await Client().GetFromJsonAsync<CampaignResponse>($"{Route}/{campaign}");
        Assert.Null(detail!.TotalVoucherLimit);
    }

    [Fact]
    public async Task PutItems_QuotaWithSingleTarget_IsRejected()
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsJsonAsync(
            ItemsRoute(campaign), Items(new { targetType = "All", benefitType = "Percentage", benefitValue = 10, voucherLimit = 100 }));

        await AssertBusinessRuleAsync(response, "CAMPAIGN_ITEM_QUOTA_SINGLE_TARGET");
    }

    [Fact]
    public async Task PutQuantities_Scenario4_PerCustomerLimitIsStoredIndependently()
    {
        var campaign = await CreateCampaignAsync();

        var body = await PutQuantitiesAsync(campaign, new { totalVoucherLimit = 5000, perCustomerVoucherLimit = 2 });
        Assert.Equal(2, body.PerCustomerVoucherLimit);
        Assert.Equal(5000, body.TotalVoucherLimit);

        var above = await PutQuantitiesAsync(campaign, new { totalVoucherLimit = 10, perCustomerVoucherLimit = 50 });
        Assert.Equal(50, above.PerCustomerVoucherLimit);

        await using var scope = _factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Campaigns.AsNoTracking().SingleAsync(c => c.Id == campaign);
        Assert.Equal(50, stored.PerCustomerVoucherLimit);
        Assert.Equal(10, stored.TotalVoucherLimit);
    }

    [Theory]
    [InlineData("""{}""", "TotalVoucherLimit", CampaignContract.TotalVoucherLimitRequired)]
    [InlineData("""{"totalVoucherLimit":0}""", "TotalVoucherLimit", CampaignContract.TotalVoucherLimitPositive)]
    [InlineData("""{"totalVoucherLimit":-1}""", "TotalVoucherLimit", CampaignContract.TotalVoucherLimitPositive)]
    [InlineData("""{"totalVoucherLimit":10,"perCustomerVoucherLimit":0}""", "PerCustomerVoucherLimit", CampaignContract.PerCustomerVoucherLimitPositive)]
    public async Task PutQuantities_WithInvalidContract_ReturnsValidationProblem(string json, string field, string message)
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client().PutAsync(QuantitiesRoute(campaign), Json(json));

        await AssertValidationAsync(response, field, message);
    }

    [Theory]
    [InlineData("items")]
    [InlineData("quantities")]
    public async Task Put_ForCampaignOfAnotherNetwork_ReturnsScopeForbidden(string resource)
    {
        var otherNetwork = await AddNetworkAsync();
        var foreign = await CreateCampaignAsync(Client(networkId: otherNetwork), otherNetwork);

        var response = await Client().PutAsync($"{Route}/{foreign}/{resource}", Json(ValidBody(resource)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SCOPE_FORBIDDEN", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("items")]
    [InlineData("quantities")]
    public async Task Put_WithUnknownCampaign_ReturnsNotFound(string resource)
    {
        var response = await Client().PutAsync($"{Route}/{Guid.NewGuid()}/{resource}", Json(ValidBody(resource)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NOT_FOUND", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("items")]
    [InlineData("quantities")]
    public async Task Put_WithoutTokenOrAsPlatformAdmin_IsRejected(string resource)
    {
        var campaign = await CreateCampaignAsync();

        var anonymous = await _factory.CreateClient().PutAsync($"{Route}/{campaign}/{resource}", Json(ValidBody(resource)));
        var admin = await Client(Roles.Administrador).PutAsync($"{Route}/{campaign}/{resource}", Json(ValidBody(resource)));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
        Assert.Equal("AUTH_FORBIDDEN_ROLE", (await TestAuth.ReadProblemAsync(admin)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Put_ByGestor_ReturnsOk()
    {
        var campaign = await CreateCampaignAsync();

        var response = await Client(Roles.Gestor).PutAsync(ItemsRoute(campaign), Json(ValidBody("items")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_IncludesConfiguration()
    {
        var campaign = await CreateCampaignAsync();
        await PutItemsAsync(campaign, new { targetType = "All", benefitType = "Percentage", benefitValue = 10 });
        await PutQuantitiesAsync(campaign, new { totalVoucherLimit = 100, perCustomerVoucherLimit = 1 });

        var list = await Client().GetFromJsonAsync<List<CampaignResponse>>($"{Route}?networkId={DemoDataSeeder.NetworkId}");
        var listed = list!.Single(item => item.Id == campaign);

        Assert.Single(listed.Items);
        Assert.Equal(100, listed.TotalVoucherLimit);
        Assert.Equal(1, listed.PerCustomerVoucherLimit);
        Assert.Equal(100, listed.UndistributedVouchers);
    }

    [Fact]
    public async Task Post_ReturnsEmptyConfiguration()
    {
        var response = await Client().PostAsJsonAsync(Route, CampaignRequest(DemoDataSeeder.NetworkId));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("undistributedVouchers").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("perCustomerVoucherLimit").ValueKind);
    }

    // Token com a hora real: o relógio fixo da Api não vale para a validação do JWT.
    private HttpClient Client(string role = Roles.Analista, Guid? networkId = null) =>
        _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, role, networkId));

    private static string ItemsRoute(Guid campaign) => $"{Route}/{campaign}/items";

    private static string QuantitiesRoute(Guid campaign) => $"{Route}/{campaign}/quantities";

    private static object Items(params object[] items) => new { items };

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static string ValidBody(string resource) => resource == "items"
        ? """{"items":[{"targetType":"All","benefitType":"Percentage","benefitValue":10}]}"""
        : """{"totalVoucherLimit":10}""";

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

    private async Task<CampaignResponse> PutItemsAsync(Guid campaign, params object[] items)
    {
        var response = await Client().PutAsJsonAsync(ItemsRoute(campaign), Items(items));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CampaignResponse>())!;
    }

    private async Task<CampaignResponse> PutQuantitiesAsync(Guid campaign, object body)
    {
        var response = await Client().PutAsJsonAsync(QuantitiesRoute(campaign), body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CampaignResponse>())!;
    }

    /// <summary>Categoria "Papelaria" e produto "Mochila Escolar G" próprios do teste (o catálogo é global).</summary>
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

    private async Task AssertNoItemsAsync(Guid campaign)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(await context.CampaignItems.AnyAsync(item => item.CampaignId == campaign));
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
