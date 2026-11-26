using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.DTOs.Catalog;
using VoucherCampaigns.Application.Eligibility;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.IntegrationTests.Authentication;
using VoucherCampaigns.IntegrationTests.Campaigns;
using static VoucherCampaigns.IntegrationTests.Vouchers.MotorScenario;

namespace VoucherCampaigns.IntegrationTests.Vouchers;

/// <summary>Público restrito no portal (spec GH-075): catálogo dos critérios e a categoria do histórico com 422.</summary>
public class EligibilityCatalogEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string CatalogRoute = "/api/v1/eligibility/criteria";

    /// <summary>Critério mínimo de cada tipo, para comparar os atributos do catálogo com os da campanha no satélite.</summary>
    private static readonly Dictionary<string, object> Samples = new()
    {
        ["AgeRange"] = new { type = "AgeRange", minAge = 18 },
        ["Location"] = new { type = "Location", states = new[] { "SP" }, cityCodes = new[] { "3550308" } },
        ["LoyaltyMember"] = new { type = "LoyaltyMember" },
        ["TotalSpent"] = new { type = "TotalSpent", minAmount = 100m, windowDays = 90 },
        ["PurchaseFrequency"] = new { type = "PurchaseFrequency", minPurchases = 2, windowDays = 60 },
        ["PurchasedCategory"] = new { type = "PurchasedCategory", categoryId = Infrastructure.Persistence.Seed.DemoDataSeeder.FashionCategoryId, windowDays = 30 },
        ["SpecificCustomers"] = new { type = "SpecificCustomers", documents = new[] { TestCpf.New() } }
    };

    [Fact]
    public async Task Catalog_DescribesEveryCriterionType_WithTheValidationLimits()
    {
        var catalog = (await User(factory).GetFromJsonAsync<EligibilityCatalogResponse>(CatalogRoute))!;

        Assert.Equal(EligibilityContract.MaxCriteria, catalog.MaxCriteria);
        Assert.Equal(Enum.GetNames<EligibilityCriterionType>(), catalog.Criteria.Select(criterion => criterion.Type));
        Assert.All(catalog.Criteria, criterion => Assert.False(string.IsNullOrWhiteSpace(criterion.Label)));

        var age = Field(catalog, "AgeRange", "maxAge");
        Assert.Equal((0m, (decimal)EligibilityContract.MaxAge), (age.Min, age.Max));
        Assert.Equal(EligibilityContract.MaxLocationEntries, Field(catalog, "Location", "cityCodes").MaxItems);
        Assert.Equal(EligibilityContract.MaxDocuments, Field(catalog, "SpecificCustomers", "documents").MaxItems);
        Assert.Equal((decimal)EligibilityContract.MaxWindowDays, Field(catalog, "TotalSpent", "windowDays").Max);
        Assert.Equal(["TotalSpent", "PurchaseFrequency", "PurchasedCategory"],
            catalog.Criteria.Where(criterion => criterion.RequiresHistoryWindow).Select(criterion => criterion.Type));
    }

    [Fact]
    public async Task CatalogAttributes_MatchTheRequiredAttributesTheSatelliteReceives()
    {
        var catalog = (await User(factory).GetFromJsonAsync<EligibilityCatalogResponse>(CatalogRoute))!;

        foreach (var criterion in catalog.Criteria)
        {
            var scenario = await ApprovedCampaignAsync(factory, (analista, id) =>
                analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/audience", new { audienceType = "Restricted", criteria = new[] { Samples[criterion.Type] } }));

            using var listed = JsonDocument.Parse(await Satellite(factory).GetStringAsync($"{SatelliteCampaignsRoute}?establishmentId={scenario.StoreId}"));
            var campaign = listed.RootElement.EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == scenario.CampaignId);
            var required = campaign.GetProperty("requiredAttributes").EnumerateArray().Select(item => item.GetString()!).ToList();

            Assert.Equal("Restricted", campaign.GetProperty("audience").GetString());
            Assert.Equal(criterion.SatelliteAttributes, required);
        }
    }

    [Fact]
    public async Task PurchasedCategory_UnknownCategory_Returns422WithTheField_AndAnInactiveOneIsAccepted()
    {
        factory.Clock.UtcNow = Setup;
        var analista = User(factory);
        var id = await ReadyCampaign.CreateAsync(analista);

        var unknown = await PutCategoryAsync(analista, id, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(unknown);
        Assert.Equal("CAMPAIGN_AUDIENCE_CATEGORY_NOT_FOUND", problem.GetProperty("code").GetString());
        Assert.Equal("Criteria[0].CategoryId", problem.GetProperty("field").GetString());
        Assert.Equal(EligibilityContract.CategoryNotFound, problem.GetProperty("detail").GetString());
        Assert.Equal(0, await QueryAsync(factory, db => db.CampaignEligibilityCriteria.CountAsync(criterion => criterion.CampaignId == id)));

        var admin = User(factory, Roles.Administrador);
        var created = await admin.PostAsJsonAsync("/api/v1/categories", new { name = $"Inativa {Guid.NewGuid():N}"[..20] });
        created.EnsureSuccessStatusCode();
        var category = (await created.Content.ReadFromJsonAsync<CategoryResponse>())!;
        (await admin.DeleteAsync($"/api/v1/categories/{category.Id}")).EnsureSuccessStatusCode();

        var inactive = await PutCategoryAsync(analista, id, category.Id);
        Assert.Equal(HttpStatusCode.OK, inactive.StatusCode);
    }

    [Fact]
    public async Task Catalog_IsForAnalystsAndManagers()
    {
        Assert.Equal(HttpStatusCode.OK, (await User(factory, Roles.Gestor).GetAsync(CatalogRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await User(factory, Roles.Administrador).GetAsync(CatalogRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(CatalogRoute)).StatusCode);
    }

    private static EligibilityFieldCatalogResponse Field(EligibilityCatalogResponse catalog, string type, string name) =>
        catalog.Criteria.Single(criterion => criterion.Type == type).Fields.Single(field => field.Name == name);

    private static Task<HttpResponseMessage> PutCategoryAsync(HttpClient analista, Guid id, Guid categoryId) =>
        analista.PutAsJsonAsync($"{ReadyCampaign.Route}/{id}/audience", new
        {
            audienceType = "Restricted",
            criteria = new[] { new { type = "PurchasedCategory", categoryId, windowDays = 90 } }
        });
}
