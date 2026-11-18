using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.DTOs.Catalog;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;
using VoucherCampaigns.IntegrationTests.Vouchers;

namespace VoucherCampaigns.IntegrationTests.Organization;

/// <summary>Importação em lote de lojas e produtos (spec GH-066, D-2 e D-3; pontos 10 e 3 do cliente).</summary>
public class BulkImportEndpointTests : IClassFixture<ApiFactory>
{
    private const string StoresRoute = "/api/v1/establishments/import";
    private const string ProductsRoute = "/api/v1/products/import";
    private readonly ApiFactory _factory;

    public BulkImportEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(-3));
    }

    [Fact]
    public async Task ImportStores_CreatesAllLines_WithTheChannelsOfTheBatch()
    {
        var prefix = Prefix();
        var lines = Enumerable.Range(1, 50).Select(index => new { code = $"{prefix}{index:D3}", name = $"Loja {index}" }).ToArray();

        var response = await Admin().PostAsJsonAsync(StoresRoute, new { regionId = DemoDataSeeder.RegionId, channels = new[] { "Physical", "Virtual" }, establishments = lines });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ImportEstablishmentsResponse>();
        Assert.Equal(50, body!.Created);
        Assert.All(body.Establishments, store => Assert.Equal(["Physical", "Virtual"], store.Channels));
        Assert.Equal(50, await MotorScenario.QueryAsync(_factory, db => db.Establishments.CountAsync(store => store.Code.StartsWith(prefix))));
    }

    [Fact]
    public async Task ImportStores_WithACodeRepeatedInTheBatch_PointsTheLineAndSavesNothing()
    {
        var prefix = Prefix();
        var response = await Admin().PostAsJsonAsync(StoresRoute, new
        {
            regionId = DemoDataSeeder.RegionId,
            channels = new[] { "Physical" },
            establishments = new[] { new { code = $"{prefix}1", name = "A" }, new { code = $"{prefix}2", name = "B" }, new { code = $"{prefix}1", name = "C" } }
        });

        await AssertValidationAsync(response, "Establishments[2].Code", OrganizationContract.ImportCodeRepeated);
        Assert.Equal(0, await MotorScenario.QueryAsync(_factory, db => db.Establishments.CountAsync(store => store.Code.StartsWith(prefix))));
    }

    [Fact]
    public async Task ImportStores_WithACodeThatAlreadyExists_RefusesTheWholeBatch()
    {
        var prefix = Prefix();
        (await Admin().PostAsJsonAsync(StoresRoute, new { regionId = DemoDataSeeder.RegionId, channels = new[] { "Physical" }, establishments = new[] { new { code = $"{prefix}1", name = "A" } } }))
            .EnsureSuccessStatusCode();

        var response = await Admin().PostAsJsonAsync(StoresRoute, new
        {
            regionId = DemoDataSeeder.RegionId,
            channels = new[] { "Physical" },
            establishments = new[] { new { code = $"{prefix}2", name = "B" }, new { code = $"{prefix}1", name = "A de novo" } }
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response);
        Assert.Equal("ESTABLISHMENT_CODE_TAKEN", problem.GetProperty("code").GetString());
        Assert.Equal($"{prefix}1", Assert.Single(problem.GetProperty("codes").EnumerateArray()).GetString());
        Assert.Equal(1, await MotorScenario.QueryAsync(_factory, db => db.Establishments.CountAsync(store => store.Code.StartsWith(prefix))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_001)]
    public async Task ImportStores_WithoutLinesOrAboveTheLimit_Returns400(int count)
    {
        var lines = Enumerable.Range(0, count).Select(index => new { code = $"X{index}", name = "Loja" }).ToArray();

        var response = await Admin().PostAsJsonAsync(StoresRoute, new { regionId = DemoDataSeeder.RegionId, channels = new[] { "Physical" }, establishments = lines });

        await AssertValidationAsync(response, "Establishments", OrganizationContract.ImportEstablishmentsRequired);
    }

    [Fact]
    public async Task ImportStores_ByAnalista_IsForbidden_LikeTheSingleRegistration()
    {
        var response = await Client(Roles.Analista).PostAsJsonAsync(StoresRoute, new
        {
            regionId = DemoDataSeeder.RegionId, channels = new[] { "Physical" }, establishments = new[] { new { code = "Z1", name = "Z" } }
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ImportProducts_CreatesAllLines_AndRefusesAnExistingCode()
    {
        var prefix = Prefix();
        var lines = Enumerable.Range(1, 50).Select(index => new { code = $"{prefix}{index:D3}", name = $"Produto {index}" }).ToArray();

        var response = await Admin().PostAsJsonAsync(ProductsRoute, new { categoryId = DemoDataSeeder.FashionCategoryId, products = lines });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ImportProductsResponse>();
        Assert.Equal(50, body!.Created);
        Assert.All(body.Products, product => Assert.Equal(DemoDataSeeder.FashionCategoryId, product.CategoryId));

        var again = await Admin().PostAsJsonAsync(ProductsRoute, new
        {
            categoryId = DemoDataSeeder.FashionCategoryId, products = new[] { new { code = $"{prefix}999", name = "Novo" }, new { code = $"{prefix}001", name = "Repetido" } }
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);
        Assert.Equal("PRODUCT_CODE_TAKEN", (await TestAuth.ReadProblemAsync(again)).GetProperty("code").GetString());
        Assert.Equal(50, await MotorScenario.QueryAsync(_factory, db => db.Products.CountAsync(product => product.Code.StartsWith(prefix))));
    }

    [Fact]
    public async Task ImportProducts_WithAnEmptyName_PointsTheLine()
    {
        var response = await Admin().PostAsJsonAsync(ProductsRoute, new
        {
            categoryId = DemoDataSeeder.FashionCategoryId, products = new[] { new { code = Prefix() + "1", name = "Ok" }, new { code = Prefix() + "2", name = "  " } }
        });

        await AssertValidationAsync(response, "Products[1].Name", CatalogContract.ProductNameRequired);
    }

    private static string Prefix() => $"I{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

    private HttpClient Client(string role) => _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, role, DemoDataSeeder.NetworkId));

    private HttpClient Admin() => _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, Roles.Administrador, null));

    private static async Task AssertValidationAsync(HttpResponseMessage response, string field, string message)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response);
        Assert.Equal("VALIDATION_ERROR", problem.GetProperty("code").GetString());
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out JsonElement messages), $"sem erro em {field}: {errors}");
        Assert.Contains(messages.EnumerateArray(), entry => entry.GetString() == message);
    }
}
