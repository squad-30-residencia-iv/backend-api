using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Application.Audit;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Infrastructure.Persistence;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;
using VoucherCampaigns.IntegrationTests.Vouchers;
using static VoucherCampaigns.IntegrationTests.Vouchers.MotorScenario;

namespace VoucherCampaigns.IntegrationTests.Campaigns;

/// <summary>Desconto único por campanha e alvo pelo código do produto (spec GH-068; PA-1 e PA-2 da GH-066).</summary>
public class SingleDiscountAndProductCodeEndpointTests : IClassFixture<ApiFactory>
{
    private const string ProductsImportRoute = "/api/v1/products/import";
    private readonly ApiFactory _factory;

    public SingleDiscountAndProductCodeEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = Setup;
    }

    [Theory]
    [InlineData("Percentage", 20)]
    [InlineData("FixedAmount", 15)]
    public async Task PutItems_WithDifferentBenefits_Returns422_AndKeepsTheCurrentItems(string secondType, decimal secondValue)
    {
        var campaign = await DraftAsync();

        var response = await Analista().PutAsJsonAsync(ItemsRoute(campaign), new
        {
            items = new object[]
            {
                new { targetType = "Category", categoryId = DemoDataSeeder.FashionCategoryId, benefitType = "Percentage", benefitValue = 15 },
                new { targetType = "Product", productCode = "MOD-001", benefitType = secondType, benefitValue = secondValue }
            }
        });

        await AssertBusinessRuleAsync(response, CampaignErrors.BenefitNotUniformCode);
        var stored = await QueryAsync(_factory, db => db.CampaignItems.Where(item => item.CampaignId == campaign).ToListAsync());
        Assert.Equal(TargetType.All, Assert.Single(stored).TargetType);
    }

    [Fact]
    public async Task Submit_WithDifferentBenefitsStoredBeforeTheRule_Returns422()
    {
        var campaign = await DraftAsync();
        await PutItemsAsync(
            campaign,
            new { targetType = "Category", categoryId = DemoDataSeeder.FashionCategoryId, benefitType = "Percentage", benefitValue = 10 },
            new { targetType = "Product", productCode = "MOD-001", benefitType = "Percentage", benefitValue = 10 });

        // Rascunho gravado antes da regra (D-1): o banco aceita descontos diferentes.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().CampaignItems
                .Where(item => item.CampaignId == campaign && item.TargetType == TargetType.Product)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.BenefitValue, 25m));
        }

        var response = await Analista().PostAsync($"{ReadyCampaign.Route}/{campaign}/submit", null);

        await AssertBusinessRuleAsync(response, CampaignErrors.BenefitNotUniformCode);
        Assert.Equal("Draft", await CampaignStatusAsync(_factory, campaign));
    }

    [Fact]
    public async Task PutItems_ByProductCode_StoresTheCatalogProduct_IgnoringCase()
    {
        var campaign = await DraftAsync();
        var productId = await QueryAsync(_factory, db => db.Products.Where(product => product.Code == "MOD-001").Select(product => product.Id).SingleAsync());

        var body = await PutItemsAsync(campaign, new { targetType = "Product", productCode = " mod-001 ", benefitType = "Percentage", benefitValue = 10 });

        var item = Assert.Single(body.Items);
        Assert.Equal(productId, item.ProductId);
        Assert.Equal("MOD-001", item.ProductCode);

        var detail = await Analista().GetFromJsonAsync<CampaignResponse>($"{ReadyCampaign.Route}/{campaign}");
        Assert.Equal("MOD-001", Assert.Single(detail!.Items).ProductCode);
    }

    [Fact]
    public async Task PutItems_WithUnknownCodes_Returns422WithAllTheCodes()
    {
        var campaign = await DraftAsync();
        var missingA = $"X{Guid.NewGuid():N}"[..20];
        var missingB = $"Y{Guid.NewGuid():N}"[..20];

        var response = await Analista().PutAsJsonAsync(ItemsRoute(campaign), Items(
            new { targetType = "Product", productCode = missingB, benefitType = "Percentage", benefitValue = 10 },
            new { targetType = "Product", productCode = "MOD-001", benefitType = "Percentage", benefitValue = 10 },
            new { targetType = "Product", productCode = missingA, benefitType = "Percentage", benefitValue = 10 }));

        var problem = await AssertBusinessRuleAsync(response, CampaignErrors.ItemProductCodeNotFoundCode);
        Assert.Equal(new[] { missingA, missingB }, problem.GetProperty("codes").EnumerateArray().Select(code => code.GetString()));
    }

    [Fact]
    public async Task PutItems_WithAnInactiveCode_Returns422WithTheCatalogCode()
    {
        var campaign = await DraftAsync();
        var code = await AddProductAsync(active: false);

        // Informado em maiúsculas; `codes` devolve o código como está no catálogo.
        var response = await Analista().PutAsJsonAsync(ItemsRoute(campaign), Items(
            new { targetType = "Product", productCode = code.ToUpperInvariant(), benefitType = "Percentage", benefitValue = 10 }));

        var problem = await AssertBusinessRuleAsync(response, CampaignErrors.ItemProductInactiveCode);
        Assert.Equal(code, Assert.Single(problem.GetProperty("codes").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task PutItems_SameProductByIdAndByCode_IsADuplicatedTarget()
    {
        var campaign = await DraftAsync();
        var productId = await QueryAsync(_factory, db => db.Products.Where(product => product.Code == "MOD-001").Select(product => product.Id).SingleAsync());

        var response = await Analista().PutAsJsonAsync(ItemsRoute(campaign), Items(
            new { targetType = "Product", productId, benefitType = "Percentage", benefitValue = 10 },
            new { targetType = "Product", productCode = "mod-001", benefitType = "Percentage", benefitValue = 10 }));

        await AssertBusinessRuleAsync(response, CampaignErrors.DuplicatedTargetCode);
    }

    [Theory]
    [InlineData("""{"targetType":"Product","productId":"0199e3a0-0000-7000-8000-000000000999","productCode":"MOD-001","benefitType":"Percentage","benefitValue":10}""", CampaignContract.TargetProductIdAndCode)]
    [InlineData("""{"targetType":"Category","categoryId":"0199e3a0-0000-7000-8000-000000000301","productCode":"MOD-001","benefitType":"Percentage","benefitValue":10}""", CampaignContract.TargetProductCodeOnlyForProduct)]
    [InlineData("""{"targetType":"All","productCode":"MOD-001","benefitType":"Percentage","benefitValue":10}""", CampaignContract.TargetProductCodeOnlyForProduct)]
    public async Task PutItems_WithAnIncoherentProductCode_Returns400(string item, string message)
    {
        var campaign = await DraftAsync();

        var response = await Analista().PutAsync(ItemsRoute(campaign), new StringContent($$"""{"items":[{{item}}]}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await TestAuth.ReadProblemAsync(response)).GetProperty("errors");
        Assert.Contains(errors.GetProperty("Items[0].ProductCode").EnumerateArray(), entry => entry.GetString() == message);
    }

    [Fact]
    public async Task PutItems_WithAThousandImportedCodes_IsAccepted()
    {
        // Caso do cliente: a lista de produtos vem da importação em lote (GH-066, D-3) e vira os alvos da campanha.
        var prefix = $"L{Guid.NewGuid():N}"[..7].ToUpperInvariant();
        var codes = Enumerable.Range(1, CampaignContract.MaxItems).Select(index => $"{prefix}{index:D4}").ToArray();
        var admin = _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, Roles.Administrador, null));
        (await admin.PostAsJsonAsync(ProductsImportRoute, new
        {
            categoryId = DemoDataSeeder.FashionCategoryId,
            products = codes.Select(code => new { code, name = $"Produto {code}" }).ToArray()
        })).EnsureSuccessStatusCode();
        var campaign = await DraftAsync();

        var body = await PutItemsAsync(campaign, codes.Select(code => (object)new { targetType = "Product", productCode = code, benefitType = "Percentage", benefitValue = 10 }).ToArray());

        Assert.Equal(codes, body.Items.Select(item => item.ProductCode));

        // D-6: a lista não cabe na coluna da trilha (2.000 caracteres no PostgreSQL); vai o resumo.
        var payload = await QueryAsync(_factory, db => db.AuditEntries
            .Where(entry => entry.CampaignId == campaign && entry.EventType == AuditEventTypes.CampaignUpdated)
            .OrderByDescending(entry => entry.Id)
            .Select(entry => entry.Payload)
            .FirstAsync());
        Assert.True(payload!.Length <= AuditJson.MaxPayloadLength, payload);
        using var json = JsonDocument.Parse(payload);
        var after = json.RootElement.GetProperty("after");
        Assert.Equal("items", json.RootElement.GetProperty("section").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("before").GetProperty("count").GetInt32());
        Assert.Equal(CampaignContract.MaxItems, after.GetProperty("count").GetInt32());
        Assert.Equal(CampaignContract.MaxItems, after.GetProperty("targets").GetProperty("Product").GetInt32());
        Assert.Equal(CampaignContract.MaxItems, after.GetProperty("changes").GetProperty("added").GetInt32());
        Assert.Equal(1, after.GetProperty("changes").GetProperty("removed").GetInt32());
    }

    [Fact]
    public async Task TwoCampaignsOnTheSameProduct_GrantOneVoucherPerPurchase()
    {
        // PA-2 da GH-066: o produto pode ter desconto em mais de uma campanha, mas só um voucher por compra (D-4).
        var storeId = await NewStoreAsync(_factory);
        var first = await ApprovedOnProductAsync(storeId, 10);
        var second = await ApprovedOnProductAsync(storeId, 20);
        _factory.Clock.UtcNow = Now;
        var document = TestCpf.New();

        var granted = await GrantedAsync(Satellite(_factory), Purchase(storeId, document: document));

        Assert.Equal(second, granted.Voucher!.CampaignId);
        Assert.Equal(1, await QueryAsync(_factory, db => db.Vouchers.CountAsync(voucher =>
            voucher.CustomerDocument == document && (voucher.CampaignId == first || voucher.CampaignId == second))));
    }

    private async Task<Guid> ApprovedOnProductAsync(Guid storeId, decimal percentage)
    {
        _factory.Clock.UtcNow = Setup;
        var analista = Analista();
        var id = await ReadyCampaign.CreateAsync(analista);
        await PutItemsAsync(id, new { targetType = "Product", productCode = "MOD-001", benefitType = "Percentage", benefitValue = percentage });
        (await analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/scopes", new { scopes = new[] { new { establishmentId = storeId, channels = new[] { "Physical" } } } }))
            .EnsureSuccessStatusCode();
        (await analista.PostAsync($"{ReadyCampaign.Route}/{id}/submit", null)).EnsureSuccessStatusCode();
        (await User(_factory, Roles.Gestor).PostAsync($"{ReadyCampaign.Route}/{id}/approve", null)).EnsureSuccessStatusCode();

        return id;
    }

    private HttpClient Analista() => User(_factory);

    private static string ItemsRoute(Guid campaign) => $"{ReadyCampaign.Route}/{campaign}/items";

    private static object Items(params object[] items) => new { items };

    private Task<Guid> DraftAsync() => ReadyCampaign.CreateAsync(Analista());

    private async Task<CampaignResponse> PutItemsAsync(Guid campaign, params object[] items)
    {
        var response = await Analista().PutAsJsonAsync(ItemsRoute(campaign), Items(items));

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<CampaignResponse>())!;
    }

    private async Task<string> AddProductAsync(bool active)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = new Product { CategoryId = DemoDataSeeder.FashionCategoryId, Code = $"P{Guid.NewGuid():N}"[..20], Name = "Produto do teste", IsActive = active };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        return product.Code;
    }

    private static async Task<JsonElement> AssertBusinessRuleAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response);
        Assert.Equal(code, problem.GetProperty("code").GetString());

        return problem;
    }
}
