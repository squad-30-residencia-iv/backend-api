using System.Net;
using System.Net.Http.Json;
using System.Text;
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

    // ---- Papéis e escopo -------------------------------------------------------------------------------------

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

        await AssertForbiddenRoleAsync(response);
    }

    [Theory]
    [InlineData("POST", "/api/v1/networks")]
    [InlineData("PUT", "/api/v1/networks/0199e3a0-0000-7000-8000-000000000001")]
    [InlineData("DELETE", "/api/v1/networks/0199e3a0-0000-7000-8000-000000000001")]
    [InlineData("POST", "/api/v1/regions")]
    [InlineData("PUT", "/api/v1/regions/0199e3a0-0000-7000-8000-000000000101")]
    [InlineData("DELETE", "/api/v1/regions/0199e3a0-0000-7000-8000-000000000101")]
    [InlineData("POST", "/api/v1/establishments")]
    [InlineData("PUT", "/api/v1/establishments/0199e3a0-0000-7000-8000-000000000201")]
    [InlineData("DELETE", "/api/v1/establishments/0199e3a0-0000-7000-8000-000000000201")]
    public async Task HierarchyWrites_ByAnalistaOrGestor_ReturnForbiddenRole(string method, string url)
    {
        foreach (var role in new[] { Roles.Analista, Roles.Gestor })
        {
            var request = new HttpRequestMessage(new HttpMethod(method), url)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };

            await AssertForbiddenRoleAsync(await Client(role).SendAsync(request));
        }
    }

    [Fact]
    public async Task Administrador_CannotUseCampaignsOrCatalog()
    {
        var campaign = await Admin().PostAsJsonAsync("/api/v1/campaigns", new { });
        var categories = await Admin().GetAsync("/api/v1/categories");

        await AssertForbiddenRoleAsync(campaign);
        await AssertForbiddenRoleAsync(categories);
    }

    [Fact]
    public async Task Analista_ReadsOwnHierarchy_AndSeesStoreChannels()
    {
        var regions = await Client().GetFromJsonAsync<List<RegionResponse>>($"/api/v1/regions?networkId={DemoDataSeeder.NetworkId}");
        var stores = await Client(Roles.Gestor).GetFromJsonAsync<List<EstablishmentResponse>>(
            $"/api/v1/establishments?regionId={DemoDataSeeder.RegionId}");
        var online = await Client().GetFromJsonAsync<EstablishmentResponse>($"/api/v1/establishments/{DemoDataSeeder.StoreOnlineId}");

        Assert.Contains(regions!, region => region.Id == DemoDataSeeder.RegionId && region.Name == "Região Norte");
        Assert.Equal(3, stores!.Count);
        Assert.Equal(["Virtual"], online!.Channels);
    }

    [Fact]
    public async Task Networks_ForAnalista_ListsOnlyTheCallerNetwork_WithTimeZone()
    {
        var networks = await Client().GetFromJsonAsync<List<NetworkResponse>>("/api/v1/networks");

        var network = Assert.Single(networks!);
        Assert.Equal(DemoDataSeeder.NetworkId, network.Id);
        Assert.Equal("America/Sao_Paulo", network.TimeZone);
    }

    [Fact]
    public async Task Networks_ActiveOnlyFalse_HidesTheActiveCallerNetwork()
    {
        var networks = await Client().GetFromJsonAsync<List<NetworkResponse>>("/api/v1/networks?activeOnly=false");

        Assert.Empty(networks!);
    }

    [Fact]
    public async Task Networks_ForAdministrador_ListsEveryNetwork()
    {
        var created = await CreateNetworkAsync(Unique("Rede"));

        var networks = await Admin().GetFromJsonAsync<List<NetworkResponse>>("/api/v1/networks");

        Assert.Contains(networks!, network => network.Id == DemoDataSeeder.NetworkId);
        Assert.Contains(networks!, network => network.Id == created.Id);
    }

    [Fact]
    public async Task HierarchyReads_OutsideCallerNetwork_ReturnScopeForbidden()
    {
        var foreignNetwork = await CreateNetworkAsync(Unique("Rede"));
        var foreignRegion = await CreateRegionAsync(foreignNetwork.Id, Unique("Regiao"));
        var foreignStore = await CreateEstablishmentAsync(foreignRegion.Id, UniqueCode(), Unique("Loja"), "Physical");

        string[] urls =
        [
            $"/api/v1/networks/{foreignNetwork.Id}",
            $"/api/v1/regions?networkId={foreignNetwork.Id}",
            $"/api/v1/regions/{foreignRegion.Id}",
            $"/api/v1/establishments?regionId={foreignRegion.Id}",
            $"/api/v1/establishments/{foreignStore.Id}"
        ];

        foreach (var url in urls)
        {
            var response = await Client().GetAsync(url);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("SCOPE_FORBIDDEN", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
        }

        var asAdmin = await Admin().GetAsync($"/api/v1/establishments/{foreignStore.Id}");
        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
    }

    // ---- Rede ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task CreateNetwork_WithInvalidTimeZone_ReturnsValidationProblem()
    {
        var response = await Admin().PostAsJsonAsync("/api/v1/networks", new { name = Unique("Rede"), timeZone = "Planeta/Marte" });

        await AssertValidationAsync(response, "TimeZone", "Informe um fuso IANA válido.");
    }

    [Fact]
    public async Task CreateNetwork_WithoutTimeZone_ReturnsValidationProblem()
    {
        var response = await Admin().PostAsJsonAsync("/api/v1/networks", new { name = Unique("Rede") });

        await AssertValidationAsync(response, "TimeZone", "Informe o fuso da rede.");
    }

    [Fact]
    public async Task CreateNetwork_ReturnsLocationTimeZoneAndClockTimestamp()
    {
        var response = await Admin().PostAsJsonAsync("/api/v1/networks", new { name = Unique("Rede"), timeZone = "America/Manaus" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<NetworkResponse>();
        Assert.NotNull(created);
        Assert.Equal("America/Manaus", created.TimeZone);
        Assert.True(created.IsActive);
        Assert.Equal(_factory.Clock.GetUtcNow().UtcDateTime, created.CreatedAt);
        Assert.Equal($"/api/v1/networks/{created.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task UpdateNetwork_WithoutIsActive_ReturnsValidationProblem()
    {
        var response = await Admin().PutAsJsonAsync(
            $"/api/v1/networks/{DemoDataSeeder.NetworkId}",
            new { name = "Supermercados Norte", timeZone = "America/Sao_Paulo" });

        await AssertValidationAsync(response, "IsActive", "Informe se a rede está ativa.");
    }

    // ---- US-02.1 ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task US0201_Scenario1_AdministradorRegistersTheFullStructure_AllActive()
    {
        var network = await CreateNetworkAsync(Unique("Supermercados Norte"));
        var region = await CreateRegionAsync(network.Id, "Nordeste");
        var store = await CreateEstablishmentAsync(region.Id, UniqueCode(), "Loja Aracaju Centro", "Physical");

        Assert.Equal(network.Id, region.NetworkId);
        Assert.Equal(region.Id, store.RegionId);
        Assert.True(network.IsActive && region.IsActive && store.IsActive);

        var stored = await Admin().GetFromJsonAsync<EstablishmentResponse>($"/api/v1/establishments/{store.Id}");
        Assert.Equal(region.Id, stored!.RegionId);
    }

    [Fact]
    public async Task US0201_Scenario2_StoreWithUnknownRegion_IsRefused()
    {
        var response = await Admin().PostAsJsonAsync(
            "/api/v1/establishments",
            new { regionId = Guid.CreateVersion7(), code = UniqueCode(), name = Unique("Loja"), channels = new[] { "Physical" } });

        await AssertBusinessRuleAsync(response, "ESTABLISHMENT_REGION_NOT_FOUND", "Região informada não encontrada");
    }

    [Fact]
    public async Task US0201_Scenario3_PhysicalOnlyStore_ListsOnlyThePhysicalChannel()
    {
        var region = await CreateRegionAsync(DemoDataSeeder.NetworkId, Unique("Regiao"));
        var store = await CreateEstablishmentAsync(region.Id, UniqueCode(), "Loja Aracaju Centro", "physical");

        var stored = await Client().GetFromJsonAsync<EstablishmentResponse>($"/api/v1/establishments/{store.Id}");

        Assert.Equal(["Physical"], stored!.Channels);
    }

    [Fact]
    public async Task UpdateStoreChannels_ReplacesTheEnabledSet_ReusingRows()
    {
        var region = await CreateRegionAsync(DemoDataSeeder.NetworkId, Unique("Regiao"));
        var code = UniqueCode();
        var store = await CreateEstablishmentAsync(region.Id, code, Unique("Loja"), "Physical");

        var both = await UpdateEstablishmentAsync(store.Id, code, store.Name, true, "Physical", "Virtual");
        var virtualOnly = await UpdateEstablishmentAsync(store.Id, code, store.Name, true, "Virtual");
        var back = await UpdateEstablishmentAsync(store.Id, code, store.Name, true, "Physical");

        Assert.Equal(["Physical", "Virtual"], both.Channels);
        Assert.Equal(["Virtual"], virtualOnly.Channels);
        Assert.Equal(["Physical"], back.Channels);

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await context.Channels.Where(channel => channel.EstablishmentId == store.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.True(rows.Single(channel => channel.Type == ChannelType.Physical).IsActive);
        Assert.False(rows.Single(channel => channel.Type == ChannelType.Virtual).IsActive);
    }

    [Theory]
    [InlineData("""{"regionId":"0199e3a0-0000-7000-8000-000000000101","code":"X-1","name":"Loja"}""", "Informe ao menos um canal do estabelecimento.")]
    [InlineData("""{"regionId":"0199e3a0-0000-7000-8000-000000000101","code":"X-1","name":"Loja","channels":[]}""", "Informe ao menos um canal do estabelecimento.")]
    [InlineData("""{"regionId":"0199e3a0-0000-7000-8000-000000000101","code":"X-1","name":"Loja","channels":["Satelite"]}""", "Tipo de canal inválido. Use 'Physical' ou 'Virtual'.")]
    [InlineData("""{"regionId":"0199e3a0-0000-7000-8000-000000000101","code":"X-1","name":"Loja","channels":["Virtual","virtual"]}""", "Os canais do estabelecimento não podem se repetir.")]
    public async Task CreateStore_WithInvalidChannels_ReturnsValidationProblem(string json, string message)
    {
        var response = await Admin().PostAsync("/api/v1/establishments", new StringContent(json, Encoding.UTF8, "application/json"));

        await AssertValidationAsync(response, "Channels", message);
    }

    [Fact]
    public async Task CreateRegion_WithUnknownNetwork_IsRefused()
    {
        var response = await Admin().PostAsJsonAsync("/api/v1/regions", new { networkId = Guid.CreateVersion7(), name = Unique("Regiao") });

        await AssertBusinessRuleAsync(response, OrganizationErrors.RegionNetworkNotFoundCode, "Rede informada não encontrada");
    }

    [Fact]
    public async Task CreateRegion_WithEmptyNetwork_ReturnsValidationProblem()
    {
        var json = $$"""{"networkId":"{{Guid.Empty}}","name":"Regiao"}""";

        var response = await Admin().PostAsync("/api/v1/regions", new StringContent(json, Encoding.UTF8, "application/json"));

        await AssertValidationAsync(response, "NetworkId", "Informe a rede.");
    }

    [Fact]
    public async Task CreateRegion_WithDuplicateName_ReturnsBusinessRule()
    {
        var response = await Admin().PostAsJsonAsync("/api/v1/regions", new { networkId = DemoDataSeeder.NetworkId, name = "Região Norte" });

        await AssertBusinessRuleAsync(response, OrganizationErrors.RegionNameTakenCode, OrganizationErrors.RegionNameTakenMessage);
    }

    [Fact]
    public async Task Regions_WithoutNetwork_ReturnsValidationProblem()
    {
        var response = await Client().GetAsync("/api/v1/regions");

        await AssertValidationAsync(response, "networkId", "Informe a rede.");
    }

    // ---- US-02.2 ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task US0202_Scenario1_InactivatingTheRegion_InactivatesItsThreeStores()
    {
        var region = await CreateRegionAsync(DemoDataSeeder.NetworkId, Unique("Nordeste"));
        var stores = new List<EstablishmentResponse>();

        for (var index = 0; index < 3; index++)
        {
            stores.Add(await CreateEstablishmentAsync(region.Id, UniqueCode(), Unique("Loja"), "Physical"));
        }

        var delete = await Admin().DeleteAsync($"/api/v1/regions/{region.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var listed = await Client().GetFromJsonAsync<List<EstablishmentResponse>>($"/api/v1/establishments?regionId={region.Id}");
        Assert.Equal(3, listed!.Count);
        Assert.All(listed, store => Assert.False(store.IsActive));

        var reactivate = await Admin().PutAsJsonAsync(
            $"/api/v1/establishments/{stores[0].Id}",
            new { code = stores[0].Code, name = stores[0].Name, isActive = true, channels = new[] { "Physical" } });
        await AssertBusinessRuleAsync(reactivate, OrganizationErrors.EstablishmentRegionInactiveCode, "Região informada está inativa");
    }

    [Fact]
    public async Task InactivatingTheNetwork_CascadesToRegionsAndStores_KeepingChannels()
    {
        var network = await CreateNetworkAsync(Unique("Rede"));
        var region = await CreateRegionAsync(network.Id, Unique("Regiao"));
        var store = await CreateEstablishmentAsync(region.Id, UniqueCode(), Unique("Loja"), "Physical", "Virtual");

        var delete = await Admin().DeleteAsync($"/api/v1/networks/{network.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var storedRegion = await Admin().GetFromJsonAsync<RegionResponse>($"/api/v1/regions/{region.Id}");
        var storedStore = await Admin().GetFromJsonAsync<EstablishmentResponse>($"/api/v1/establishments/{store.Id}");
        Assert.False(storedRegion!.IsActive);
        Assert.False(storedStore!.IsActive);
        Assert.Equal(["Physical", "Virtual"], storedStore.Channels);

        var reactivate = await Admin().PutAsJsonAsync($"/api/v1/regions/{region.Id}", new { name = region.Name, isActive = true });
        await AssertBusinessRuleAsync(reactivate, OrganizationErrors.RegionNetworkInactiveCode, "Rede informada está inativa");

        var inInactiveNetwork = await Admin().PostAsJsonAsync("/api/v1/regions", new { networkId = network.Id, name = Unique("Regiao") });
        await AssertBusinessRuleAsync(inInactiveNetwork, OrganizationErrors.RegionNetworkInactiveCode, "Rede informada está inativa");
    }

    [Fact]
    public async Task CreateStore_InInactiveRegion_ReturnsBusinessRule()
    {
        var region = await CreateRegionAsync(DemoDataSeeder.NetworkId, Unique("Inativa"));
        await Admin().DeleteAsync($"/api/v1/regions/{region.Id}");

        var response = await Admin().PostAsJsonAsync(
            "/api/v1/establishments",
            new { regionId = region.Id, code = UniqueCode(), name = Unique("Loja"), channels = new[] { "Virtual" } });

        await AssertBusinessRuleAsync(response, OrganizationErrors.EstablishmentRegionInactiveCode, "Região informada está inativa");
    }

    // ---- US-02.4 ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Categories_ByGestor_ReturnsSeededCategory()
    {
        var categories = await Client(Roles.Gestor).GetFromJsonAsync<List<CategoryResponse>>("/api/v1/categories");

        Assert.Contains(categories!, category => category.Name == "Moda" && category.IsActive);
    }

    [Fact]
    public async Task US0204_Scenarios1And3_ProductWithCategory_ThenDuplicateIdentifierIsRefused()
    {
        var category = await CreateCategoryAsync(Unique("Papelaria"));

        var created = await Client().PostAsJsonAsync(
            "/api/v1/products", new { categoryId = category.Id, code = "SKU-4471", name = "Mochila Escolar G" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var product = await created.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.Equal(category.Id, product!.CategoryId);
        Assert.Equal("SKU-4471", product.Code);

        var duplicate = await Client().PostAsJsonAsync(
            "/api/v1/products", new { categoryId = category.Id, code = "SKU-4471", name = "Outra mochila" });

        await AssertBusinessRuleAsync(duplicate, CatalogErrors.ProductCodeTakenCode, "Já existe produto com este identificador");
    }

    [Fact]
    public async Task US0204_Scenario2_ProductWithUnknownCategory_IsRefused()
    {
        var response = await Client().PostAsJsonAsync(
            "/api/v1/products", new { categoryId = Guid.CreateVersion7(), code = UniqueCode(), name = "Produto do Bazar" });

        await AssertBusinessRuleAsync(response, CatalogErrors.ProductCategoryNotFoundCode, "Categoria informada não encontrada");
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
        await AssertBusinessRuleAsync(reactivate, CatalogErrors.ProductCategoryInactiveCode, "Categoria informada está inativa");

        var inInactive = await Client().PostAsJsonAsync(
            "/api/v1/products", new { categoryId = category.Id, code = UniqueCode(), name = Unique("Produto") });
        await AssertBusinessRuleAsync(inInactive, CatalogErrors.ProductCategoryInactiveCode, "Categoria informada está inativa");
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
    public async Task CreateProduct_WithEmptyCategory_ReturnsValidationProblem()
    {
        var json = $$"""{"categoryId":"{{Guid.Empty}}","code":"{{UniqueCode()}}","name":"Sem categoria"}""";

        var response = await Client().PostAsync("/api/v1/products", new StringContent(json, Encoding.UTF8, "application/json"));

        await AssertValidationAsync(response, "CategoryId", "Informe a categoria do produto.");
    }

    // ---- Ausência --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Roles.Administrador, "networks")]
    [InlineData(Roles.Administrador, "regions")]
    [InlineData(Roles.Administrador, "establishments")]
    [InlineData(Roles.Analista, "categories")]
    [InlineData(Roles.Analista, "products")]
    public async Task UnknownId_ReturnsNotFound(string role, string resource)
    {
        var url = $"/api/v1/{resource}/{Guid.CreateVersion7()}";
        var client = role == Roles.Administrador ? Admin() : Client(role);

        var get = await client.GetAsync(url);
        var delete = await client.DeleteAsync(url);

        foreach (var response in new[] { get, delete })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("NOT_FOUND", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Listing_WithUnknownParentInQuery_ReturnsNotFound()
    {
        var regions = await Admin().GetAsync($"/api/v1/regions?networkId={Guid.CreateVersion7()}");
        var stores = await Admin().GetAsync($"/api/v1/establishments?regionId={Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, regions.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, stores.StatusCode);
    }

    // ---- Apoio -----------------------------------------------------------------------------------------------

    private HttpClient Client(string role = Roles.Analista, Guid? networkId = null) =>
        _factory.CreateClient().WithBearer(TestAuth.CreateToken(_factory, role, networkId));

    private HttpClient Admin() => Client(Roles.Administrador);

    private async Task<NetworkResponse> CreateNetworkAsync(string name)
    {
        var response = await Admin().PostAsJsonAsync("/api/v1/networks", new { name, timeZone = "America/Sao_Paulo" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<NetworkResponse>())!;
    }

    private async Task<RegionResponse> CreateRegionAsync(Guid networkId, string name)
    {
        var response = await Admin().PostAsJsonAsync("/api/v1/regions", new { networkId, name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RegionResponse>())!;
    }

    private async Task<EstablishmentResponse> CreateEstablishmentAsync(Guid regionId, string code, string name, params string[] channels)
    {
        var response = await Admin().PostAsJsonAsync("/api/v1/establishments", new { regionId, code, name, channels });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EstablishmentResponse>())!;
    }

    private async Task<EstablishmentResponse> UpdateEstablishmentAsync(
        Guid id, string code, string name, bool isActive, params string[] channels)
    {
        var response = await Admin().PutAsJsonAsync($"/api/v1/establishments/{id}", new { code, name, isActive, channels });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EstablishmentResponse>())!;
    }

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

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 1 + 32)];

    private static string UniqueCode() => Guid.NewGuid().ToString("N")[..12];

    private static async Task AssertForbiddenRoleAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("AUTH_FORBIDDEN_ROLE", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
    }

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
