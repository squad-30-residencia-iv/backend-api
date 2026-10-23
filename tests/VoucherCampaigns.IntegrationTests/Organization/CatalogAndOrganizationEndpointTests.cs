using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.DTOs.Catalog;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Infrastructure.Persistence;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;

namespace VoucherCampaigns.IntegrationTests.Organization;

public class CatalogAndOrganizationEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public CatalogAndOrganizationEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.Clock.UtcNow = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(-3));
    }

    [Fact]
    public async Task Categories_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/categories");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("AUTH_UNAUTHENTICATED", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Categories_WithUnknownRole_ReturnsForbiddenRole()
    {
        var response = await Client(role: "visitante").GetAsync("/api/v1/categories");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("AUTH_FORBIDDEN_ROLE", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Categories_ByGestor_ReturnsSeededCategory()
    {
        var response = await Client(Roles.Gestor).GetAsync("/api/v1/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categories = await response.Content.ReadFromJsonAsync<List<CategoryResponse>>();
        Assert.Contains(categories!, category => category.Name == "Moda" && category.IsActive);
    }

    [Fact]
    public async Task CreateCategory_WithBlankName_ReturnsValidationProblem()
    {
        var response = await Client().PostAsJsonAsync("/api/v1/categories", new { name = "   " });

        await AssertValidationAsync(response, "Name", "Informe o nome da categoria.");
    }

    [Fact]
    public async Task CreateCategory_WithNameTooLong_ReturnsValidationProblem()
    {
        var response = await Client().PostAsJsonAsync("/api/v1/categories", new { name = new string('a', 151) });

        await AssertValidationAsync(response, "Name", "O nome deve ter no máximo 150 caracteres.");
    }

    [Fact]
    public async Task CreateCategory_WithDuplicateName_ReturnsBusinessRule()
    {
        var response = await Client().PostAsJsonAsync("/api/v1/categories", new { name = "Moda" });

        await AssertBusinessRuleAsync(response, CatalogErrors.CategoryNameTakenCode, CatalogErrors.CategoryNameTakenMessage);
    }

    [Fact]
    public async Task CreateCategory_ReturnsLowercaseLocationAndClockTimestamp()
    {
        var name = Unique("Categoria");

        var response = await Client().PostAsJsonAsync("/api/v1/categories", new { name });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var category = await response.Content.ReadFromJsonAsync<CategoryResponse>();
        Assert.NotNull(category);
        Assert.Equal($"/api/v1/categories/{category.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(name, category.Name);
        Assert.True(category.IsActive);
        Assert.Equal(_factory.Clock.GetUtcNow().UtcDateTime, category.CreatedAt);
    }

    [Fact]
    public async Task UpdateCategory_WithoutIsActive_ReturnsValidationProblem()
    {
        var created = await CreateCategoryAsync(Unique("Sem flag"));

        var response = await Client().PutAsync(
            $"/api/v1/categories/{created.Id}",
            new StringContent($$"""{"name":"{{created.Name}}"}""", Encoding.UTF8, "application/json"));

        await AssertValidationAsync(response, "IsActive", "Informe se a categoria está ativa.");
    }

    [Fact]
    public async Task InactivateCategory_CascadesProducts_AndBlocksReactivation()
    {
        var category = await CreateCategoryAsync(Unique("Cascata"));
        var product = await CreateProductAsync(category.Id, UniqueCode(), Unique("Produto"));

        var response = await Client().DeleteAsync($"/api/v1/categories/{category.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var storedCategory = await Client().GetFromJsonAsync<CategoryResponse>($"/api/v1/categories/{category.Id}");
        var storedProduct = await Client().GetFromJsonAsync<ProductResponse>($"/api/v1/products/{product.Id}");
        Assert.False(storedCategory!.IsActive);
        Assert.False(storedProduct!.IsActive);

        var reactivate = await Client().PutAsJsonAsync(
            $"/api/v1/products/{product.Id}",
            new { code = product.Code, name = product.Name, isActive = true });

        await AssertBusinessRuleAsync(reactivate, CatalogErrors.ProductParentUnavailableCode, CatalogErrors.ProductParentUnavailableMessage);
    }

    [Fact]
    public async Task CreateProduct_WithEmptyCategory_ReturnsValidationProblem()
    {
        var json = $$"""{"categoryId":"{{Guid.Empty}}","code":"{{UniqueCode()}}","name":"Sem categoria"}""";

        var response = await Client().PostAsync("/api/v1/products", new StringContent(json, Encoding.UTF8, "application/json"));

        await AssertValidationAsync(response, "CategoryId", "Informe a categoria do produto.");
    }

    [Fact]
    public async Task Networks_ListsOnlyTheCallerNetwork_WithTimeZone()
    {
        var response = await Client().GetAsync("/api/v1/networks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var networks = await response.Content.ReadFromJsonAsync<List<NetworkResponse>>();
        var network = Assert.Single(networks!);
        Assert.Equal(DemoDataSeeder.NetworkId, network.Id);
        Assert.Equal("America/Sao_Paulo", network.TimeZone);
    }

    [Fact]
    public async Task Networks_ActiveOnlyFalse_HidesTheActiveCallerNetwork()
    {
        var response = await Client().GetAsync("/api/v1/networks?activeOnly=false");

        var networks = await response.Content.ReadFromJsonAsync<List<NetworkResponse>>();
        Assert.Empty(networks!);
    }

    [Fact]
    public async Task GetNetwork_OutsideScope_ReturnsScopeForbidden()
    {
        var response = await Client().GetAsync($"/api/v1/networks/{TestAuth.OtherNetworkId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SCOPE_FORBIDDEN", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateNetwork_WithInvalidTimeZone_ReturnsValidationProblem()
    {
        var response = await Client().PostAsJsonAsync("/api/v1/networks", new { name = Unique("Rede"), timeZone = "Planeta/Marte" });

        await AssertValidationAsync(response, "TimeZone", "Informe um fuso IANA válido.");
    }

    [Fact]
    public async Task CreateNetwork_WithoutTimeZone_ReturnsValidationProblem()
    {
        var response = await Client().PostAsJsonAsync("/api/v1/networks", new { name = Unique("Rede") });

        await AssertValidationAsync(response, "TimeZone", "Informe o fuso da rede.");
    }

    [Fact]
    public async Task CreateNetwork_ExposesTimeZone_AndStaysOutOfTheCallerList()
    {
        var name = Unique("Rede");

        var response = await Client().PostAsJsonAsync("/api/v1/networks", new { name, timeZone = "America/Manaus" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<NetworkResponse>();
        Assert.NotNull(created);
        Assert.Equal("America/Manaus", created.TimeZone);
        Assert.Equal($"/api/v1/networks/{created.Id}", response.Headers.Location?.OriginalString);

        var listed = await Client().GetFromJsonAsync<List<NetworkResponse>>("/api/v1/networks");
        Assert.DoesNotContain(listed!, network => network.Id == created.Id);

        var foreign = await Client().GetAsync($"/api/v1/networks/{created.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        var owned = await Client(networkId: created.Id).GetFromJsonAsync<NetworkResponse>($"/api/v1/networks/{created.Id}");
        Assert.Equal("America/Manaus", owned!.TimeZone);
    }

    [Fact]
    public async Task UpdateNetwork_WithoutIsActive_ReturnsValidationProblem()
    {
        var response = await Client().PutAsJsonAsync(
            $"/api/v1/networks/{DemoDataSeeder.NetworkId}",
            new { name = "Supermercados Norte", timeZone = "America/Sao_Paulo" });

        await AssertValidationAsync(response, "IsActive", "Informe se a rede está ativa.");
    }

    [Fact]
    public async Task Regions_WithoutNetwork_ReturnsValidationProblem()
    {
        var response = await Client().GetAsync("/api/v1/regions");

        await AssertValidationAsync(response, "networkId", "Informe a rede.");
    }

    [Fact]
    public async Task Regions_OutsideScope_ReturnsScopeForbidden()
    {
        var response = await Client().GetAsync($"/api/v1/regions?networkId={TestAuth.OtherNetworkId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SCOPE_FORBIDDEN", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Regions_InScope_ReturnsSeededRegion()
    {
        var response = await Client().GetAsync($"/api/v1/regions?networkId={DemoDataSeeder.NetworkId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var regions = await response.Content.ReadFromJsonAsync<List<RegionResponse>>();
        Assert.Contains(regions!, region => region.Id == DemoDataSeeder.RegionId && region.Name == "Região Norte");
    }

    [Fact]
    public async Task CreateRegion_OutsideScope_ReturnsScopeForbidden()
    {
        var response = await Client().PostAsJsonAsync(
            "/api/v1/regions", new { networkId = TestAuth.OtherNetworkId, name = Unique("Regiao") });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SCOPE_FORBIDDEN", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateRegion_WithDuplicateName_ReturnsBusinessRule()
    {
        var response = await Client().PostAsJsonAsync(
            "/api/v1/regions", new { networkId = DemoDataSeeder.NetworkId, name = "Região Norte" });

        await AssertBusinessRuleAsync(response, OrganizationErrors.RegionNameTakenCode, OrganizationErrors.RegionNameTakenMessage);
    }

    [Fact]
    public async Task InactivateRegion_CascadesEstablishmentsAndChannels_AndBlocksReactivation()
    {
        var region = await CreateRegionAsync(Unique("Regiao"));
        var code = UniqueCode();
        var establishment = await CreateEstablishmentAsync(region.Id, code, Unique("Loja"));
        var channel = await CreateChannelAsync(establishment.Id, "Physical");

        var duplicate = await Client().PostAsJsonAsync("/api/v1/channels", new { establishmentId = establishment.Id, type = "physical" });
        await AssertBusinessRuleAsync(duplicate, OrganizationErrors.ChannelTypeTakenCode, OrganizationErrors.ChannelTypeTakenMessage);

        var invalidType = await Client().PostAsJsonAsync("/api/v1/channels", new { establishmentId = establishment.Id, type = "Satelite" });
        await AssertValidationAsync(invalidType, "Type", "Tipo de canal inválido. Use 'Physical' ou 'Virtual'.");

        var delete = await Client().DeleteAsync($"/api/v1/regions/{region.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storedRegion = await context.Regions.SingleAsync(item => item.Id == region.Id);
        var storedEstablishment = await context.Establishments.Include(item => item.Channels).SingleAsync(item => item.Id == establishment.Id);
        Assert.False(storedRegion.IsActive);
        Assert.False(storedEstablishment.IsActive);
        Assert.False(Assert.Single(storedEstablishment.Channels).IsActive);

        var reactivateStore = await Client().PutAsJsonAsync(
            $"/api/v1/establishments/{establishment.Id}",
            new { code, name = establishment.Name, isActive = true });
        await AssertBusinessRuleAsync(
            reactivateStore, OrganizationErrors.EstablishmentParentUnavailableCode, OrganizationErrors.EstablishmentParentUnavailableMessage);

        var reactivateChannel = await Client().PutAsJsonAsync($"/api/v1/channels/{channel.Id}", new { isActive = true });
        await AssertBusinessRuleAsync(
            reactivateChannel, OrganizationErrors.ChannelParentUnavailableCode, OrganizationErrors.ChannelParentUnavailableMessage);
    }

    [Fact]
    public async Task GetRegion_OutsideCallerNetwork_ReturnsScopeForbidden()
    {
        var region = await CreateRegionAsync(Unique("Alheia"));

        var response = await Client(networkId: TestAuth.OtherNetworkId).GetAsync($"/api/v1/regions/{region.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SCOPE_FORBIDDEN", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateEstablishment_InInactiveRegion_ReturnsBusinessRule()
    {
        var region = await CreateRegionAsync(Unique("Inativa"));
        await Client().DeleteAsync($"/api/v1/regions/{region.Id}");

        var response = await Client().PostAsJsonAsync(
            "/api/v1/establishments", new { regionId = region.Id, code = UniqueCode(), name = Unique("Loja") });

        await AssertBusinessRuleAsync(
            response, OrganizationErrors.EstablishmentParentUnavailableCode, OrganizationErrors.EstablishmentParentUnavailableMessage);
    }

    private HttpClient Client(string role = Roles.Analista, Guid? networkId = null) =>
        _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, role, networkId));

    private async Task<CategoryResponse> CreateCategoryAsync(string name)
    {
        var response = await Client().PostAsJsonAsync("/api/v1/categories", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
    }

    private async Task<ProductResponse> CreateProductAsync(Guid categoryId, string code, string name)
    {
        var response = await Client().PostAsJsonAsync("/api/v1/products", new { categoryId, code, name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private async Task<RegionResponse> CreateRegionAsync(string name)
    {
        var response = await Client().PostAsJsonAsync("/api/v1/regions", new { networkId = DemoDataSeeder.NetworkId, name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RegionResponse>())!;
    }

    private async Task<EstablishmentResponse> CreateEstablishmentAsync(Guid regionId, string code, string name)
    {
        var response = await Client().PostAsJsonAsync("/api/v1/establishments", new { regionId, code, name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EstablishmentResponse>())!;
    }

    private async Task<ChannelResponse> CreateChannelAsync(Guid establishmentId, string type)
    {
        var response = await Client().PostAsJsonAsync("/api/v1/channels", new { establishmentId, type });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChannelResponse>())!;
    }

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 1 + 32)];

    private static string UniqueCode() => Guid.NewGuid().ToString("N")[..12];

    private static async Task AssertValidationAsync(HttpResponseMessage response, string field, string message)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response);
        Assert.Equal("VALIDATION_ERROR", problem.GetProperty("code").GetString());
        Assert.Contains(message, problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(error => error.GetString()));
    }

    private static async Task AssertBusinessRuleAsync(HttpResponseMessage response, string code, string message)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal(message, problem.GetProperty("detail").GetString());
    }
}
