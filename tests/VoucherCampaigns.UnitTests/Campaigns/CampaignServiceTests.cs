using VoucherCampaigns.Application.Audit;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.UnitTests.Campaigns;

public class CampaignServiceTests
{
    private static readonly Network Network = new() { Name = "Supermercados Norte", TimeZone = "America/Sao_Paulo" };

    // 28/09/2026 12:00 em Brasília.
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.FromHours(-3));

    private readonly FakeCampaignRepository _campaigns = new();
    private readonly FakeCatalogRepository _catalog = new();
    private readonly FakeAuditTrail _audit = new();
    private readonly FakeCurrentUser _user = new(Network.Id);

    [Fact]
    public async Task CreateAsync_ConvertsNetworkLocalTimesAndStoresDraft()
    {
        var response = await CreateService(Network).CreateAsync(
            Request(new DateTime(2026, 10, 1, 8, 0, 0), new DateTime(2026, 10, 31, 23, 59, 0)), CancellationToken.None);

        var stored = Assert.Single(_campaigns.Added);

        Assert.Equal(stored.Id, response.Id);
        Assert.Equal(new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc), stored.StartsAt);
        Assert.Equal(new DateTime(2026, 11, 1, 2, 59, 0, DateTimeKind.Utc), stored.EndsAt);
        Assert.Equal("Draft", response.Status);
        Assert.Equal("America/Sao_Paulo", response.TimeZone);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(-3)), response.StartsAt);
        Assert.Equal(Now.UtcDateTime, response.CreatedAt);
    }

    [Fact]
    public async Task CreateAsync_WithUnknownNetwork_ThrowsNetworkUnavailable()
    {
        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateService(network: null).CreateAsync(Request(new DateTime(2026, 10, 1, 8, 0, 0), new DateTime(2026, 10, 2, 8, 0, 0)), CancellationToken.None));

        Assert.Equal(CampaignErrors.NetworkUnavailableCode, exception.Code);
        Assert.Empty(_campaigns.Added);
    }

    [Fact]
    public async Task CreateAsync_WithInactiveNetwork_ThrowsNetworkUnavailable()
    {
        var inactive = new Network { Name = "Inativa", IsActive = false };

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateService(inactive).CreateAsync(Request(new DateTime(2026, 10, 1, 8, 0, 0), new DateTime(2026, 10, 2, 8, 0, 0)), CancellationToken.None));

        Assert.Equal(CampaignErrors.NetworkUnavailableCode, exception.Code);
    }

    [Fact]
    public async Task CreateAsync_WithInvalidPeriod_DoesNotStore()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateService(Network).CreateAsync(Request(new DateTime(2026, 10, 31, 8, 0, 0), new DateTime(2026, 10, 1, 8, 0, 0)), CancellationToken.None));

        Assert.Empty(_campaigns.Added);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData(" PendingApproval ")]
    public async Task GetAllAsync_AcceptsStatusNameIgnoringCase(string status)
    {
        var response = await CreateService(Network).GetAllAsync(Network.Id, status, CancellationToken.None);

        Assert.Empty(response);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("Rascunho")]
    [InlineData("")]
    public async Task GetAllAsync_WithUnknownStatus_ThrowsValidation(string status)
    {
        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            CreateService(Network).GetAllAsync(Network.Id, status, CancellationToken.None));

        Assert.Equal("Status", exception.Field);
    }

    [Fact]
    public async Task GetAllAsync_FiltersByEffectiveStatus()
    {
        var campaign = StoredDraft();
        campaign.Status = CampaignStatus.Scheduled;
        var duringPeriod = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.FromHours(-3));

        var actives = await CreateService(Network, duringPeriod).GetAllAsync(Network.Id, "active", CancellationToken.None);
        var scheduled = await CreateService(Network, duringPeriod).GetAllAsync(Network.Id, "Scheduled", CancellationToken.None);

        Assert.Equal("Active", Assert.Single(actives).Status);
        Assert.Empty(scheduled);
    }

    [Fact]
    public async Task SubmitAsync_AppendsAuditEntryWithActorScopeAndPayload()
    {
        var campaign = StoredReadyDraft();

        await CreateService(Network).SubmitAsync(campaign.Id, CancellationToken.None);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditEventTypes.CampaignSubmitted, entry.EventType);
        Assert.Equal(AuditActorType.User, entry.ActorType);
        Assert.Equal(_user.UserId, entry.ActorId);
        Assert.Equal(Network.Id, entry.NetworkId);
        Assert.Equal(campaign.Id, entry.CampaignId);
        Assert.Equal("Campaign", entry.EntityType);
        Assert.Equal(campaign.Id, entry.EntityId);
        Assert.Equal(Now.UtcDateTime, entry.OccurredAt);
        Assert.Equal("""{"fromStatus":"Draft","toStatus":"PendingApproval"}""", entry.Payload);
    }

    [Fact]
    public async Task RejectAsync_AuditPayloadKeepsTheReasonReadable()
    {
        var campaign = StoredReadyDraft();
        campaign.Status = CampaignStatus.PendingApproval;

        await CreateService(Network).RejectAsync(campaign.Id, new RejectCampaignRequest(" Fora do calendário "), CancellationToken.None);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AuditEventTypes.CampaignRejected, entry.EventType);
        Assert.Equal("""{"fromStatus":"PendingApproval","toStatus":"Rejected","reason":"Fora do calendário"}""", entry.Payload);
    }

    [Fact]
    public async Task SubmitAsync_WhenIncomplete_DoesNotAppendAudit()
    {
        var campaign = StoredDraft();

        await Assert.ThrowsAsync<BusinessRuleException>(() => CreateService(Network).SubmitAsync(campaign.Id, CancellationToken.None));

        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task GetByIdAsync_ForCampaignOfAnotherNetwork_ThrowsScopeForbidden()
    {
        var other = new Network { Name = "Outra rede", TimeZone = "America/Sao_Paulo" };
        _campaigns.Added.Add(new Campaign { NetworkId = other.Id, Network = other, Name = "Fora do escopo" });

        await Assert.ThrowsAsync<ScopeForbiddenException>(() =>
            CreateService(Network).GetByIdAsync(_campaigns.Added[0].Id, CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(InvalidItems))]
    public async Task ReplaceItemsAsync_WithIncoherentItem_ThrowsValidation(CampaignItemRequest item, string field, string message)
    {
        var campaign = StoredDraft();

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            CreateService(Network).ReplaceItemsAsync(campaign.Id, new ReplaceCampaignItemsRequest([item]), CancellationToken.None));

        Assert.Equal(field, exception.Field);
        Assert.Equal(message, exception.Message);
    }

    public static TheoryData<CampaignItemRequest, string, string> InvalidItems() => new()
    {
        { new("Todos", null, null, "Percentage", 10, null), "Items[0].TargetType", CampaignContract.TargetTypeInvalid },
        { new("0", null, null, "Percentage", 10, null), "Items[0].TargetType", CampaignContract.TargetTypeInvalid },
        { new("All", Guid.NewGuid(), null, "Percentage", 10, null), "Items[0].TargetType", CampaignContract.TargetAllWithId },
        { new("Category", null, null, "Percentage", 10, null), "Items[0].CategoryId", CampaignContract.TargetCategoryRequired },
        { new("Category", Guid.Empty, null, "Percentage", 10, null), "Items[0].CategoryId", CampaignContract.TargetCategoryRequired },
        { new("Category", Guid.NewGuid(), Guid.NewGuid(), "Percentage", 10, null), "Items[0].CategoryId", CampaignContract.TargetCategoryRequired },
        { new("Product", null, null, "FixedAmount", 10, null), "Items[0].ProductId", CampaignContract.TargetProductRequired },
        { new("All", null, null, "Desconto", 10, null), "Items[0].BenefitType", CampaignContract.BenefitTypeInvalid },
        { new("All", null, null, "Percentage", 10.555m, null), "Items[0].BenefitValue", CampaignContract.BenefitValueScale },
        { new("All", null, null, "FixedAmount", 100_000_000m, null), "Items[0].BenefitValue", CampaignContract.BenefitValueTooLarge }
    };

    [Fact]
    public async Task ReplaceItemsAsync_AcceptsTypeNamesIgnoringCase()
    {
        var campaign = StoredDraft();

        var response = await CreateService(Network).ReplaceItemsAsync(
            campaign.Id, new ReplaceCampaignItemsRequest([new(" all ", null, null, "percentage", 10, null)]), CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("All", item.TargetType);
        Assert.Equal("Percentage", item.BenefitType);
    }

    [Fact]
    public async Task ReplaceItemsAsync_WithUnknownOrInactiveCatalogTarget_ThrowsBusinessRule()
    {
        var campaign = StoredDraft();
        var inactiveCategory = new Category { Name = "Inativa", IsActive = false };
        var inactiveProduct = new Product { Name = "Inativo", IsActive = false };
        _catalog.Categories.Add(inactiveCategory);
        _catalog.Products.Add(inactiveProduct);

        async Task<string> CodeFor(CampaignItemRequest item) =>
            (await Assert.ThrowsAsync<BusinessRuleException>(() =>
                CreateService(Network).ReplaceItemsAsync(campaign.Id, new ReplaceCampaignItemsRequest([item]), CancellationToken.None))).Code;

        Assert.Equal(CampaignErrors.ItemCategoryNotFoundCode, await CodeFor(new("Category", Guid.NewGuid(), null, "Percentage", 10, null)));
        Assert.Equal(CampaignErrors.ItemCategoryInactiveCode, await CodeFor(new("Category", inactiveCategory.Id, null, "Percentage", 10, null)));
        Assert.Equal(CampaignErrors.ItemProductNotFoundCode, await CodeFor(new("Product", null, Guid.NewGuid(), "Percentage", 10, null)));
        Assert.Equal(CampaignErrors.ItemProductInactiveCode, await CodeFor(new("Product", null, inactiveProduct.Id, "Percentage", 10, null)));
    }

    [Fact]
    public async Task ReplaceItemsAsync_ReturnsItemsInInformedOrder()
    {
        var campaign = StoredDraft();
        var category = new Category { Name = "Papelaria" };
        var product = new Product { Name = "Mochila" };
        _catalog.Categories.Add(category);
        _catalog.Products.Add(product);

        var response = await CreateService(Network).ReplaceItemsAsync(
            campaign.Id,
            new ReplaceCampaignItemsRequest(
            [
                new("Product", null, product.Id, "FixedAmount", 20, null),
                new("All", null, null, "Percentage", 5, null),
                new("Category", category.Id, null, "Percentage", 15, null)
            ]),
            CancellationToken.None);

        Assert.Equal(["Product", "All", "Category"], response.Items.Select(item => item.TargetType));
    }

    private Campaign StoredDraft()
    {
        var campaign = Campaign.CreateDraft(
            Network.Id,
            "Volta às Aulas",
            null,
            new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 1, 2, 59, 0, DateTimeKind.Utc),
            Now.UtcDateTime);
        campaign.Network = Network;
        _campaigns.Added.Add(campaign);

        return campaign;
    }

    /// <summary>Rascunho com tudo o que a submissão exige (US-01.6).</summary>
    private Campaign StoredReadyDraft()
    {
        var campaign = StoredDraft();
        var utcNow = Now.UtcDateTime;

        campaign.SetAudience(CampaignAudienceType.AllCustomers, utcNow);
        campaign.ReplaceItems([new CampaignItem { TargetType = TargetType.All, BenefitType = BenefitType.Percentage, BenefitValue = 10 }], utcNow);
        campaign.SetQuantities(100, null, utcNow);
        campaign.SetRelativeValidity(30, VoucherValidityUnit.Days, null, utcNow);
        campaign.SetUsageRestrictions(null, null, 1, utcNow);
        campaign.ReplaceScopes([new CampaignScope { ChannelType = ChannelType.Physical }], utcNow);

        return campaign;
    }

    private CampaignService CreateService(Network? network, DateTimeOffset? now = null) =>
        new(new FakeNetworkRepository(network), _campaigns, _catalog, new FakeOrganizationRepository(), _user, _audit, new FixedTimeProvider(now ?? Now));

    private static CreateCampaignRequest Request(DateTime startsAt, DateTime endsAt) =>
        new(Network.Id, "Volta às Aulas", null, startsAt, endsAt);

    private sealed class FakeNetworkRepository(Network? network) : INetworkRepository
    {
        public Task<Network?> FindByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(network);
    }

    private sealed class FakeCampaignRepository : ICampaignRepository
    {
        public List<Campaign> Added { get; } = [];

        public Task AddAsync(Campaign campaign, CancellationToken cancellationToken)
        {
            Added.Add(campaign);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Campaign>> GetByNetworkAsync(Guid networkId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Campaign>>(Added.Where(campaign => campaign.NetworkId == networkId).ToList());

        public Task<Campaign?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Added.FirstOrDefault(campaign => campaign.Id == id));

        public Task<Campaign?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken) => FindByIdAsync(id, cancellationToken);

        public void AddStatusEvent(CampaignStatusEvent statusEvent)
        {
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Só as consultas por id usadas na validação dos alvos (US-01.2).</summary>
    private sealed class FakeCatalogRepository : ICatalogRepository
    {
        public List<Category> Categories { get; } = [];

        public List<Product> Products { get; } = [];

        public Task<Category?> GetCategoryByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Categories.FirstOrDefault(category => category.Id == id));

        public Task<Product?> GetProductByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Products.FirstOrDefault(product => product.Id == id));

        public Task<IReadOnlyList<Product>> GetProductsByCodesAsync(IReadOnlyCollection<string> codes, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Product>>(
                Products.Where(product => codes.Contains(product.Code, StringComparer.OrdinalIgnoreCase)).ToList());

        public Task<IReadOnlyList<Category>> GetAllCategoriesAsync(bool? activeOnly, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Category> CreateCategoryAsync(Category category, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Category> UpdateCategoryAsync(Category category, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ExistsCategoryByNameAsync(string name, Guid? excludeId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Product>> GetProductsAsync(Guid? categoryId, bool? activeOnly, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Product> CreateProductAsync(Product product, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Product> UpdateProductAsync(Product product, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ExistsProductByCodeAsync(string code, Guid? excludeId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>A validação do escopo é coberta nos testes de integração; aqui nenhuma consulta é usada.</summary>
    private sealed class FakeOrganizationRepository : IOrganizationRepository
    {
        public Task<IReadOnlyList<Network>> GetNetworksAsync(bool? activeOnly, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Network?> GetNetworkByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Network> CreateNetworkAsync(Network network, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Network> UpdateNetworkAsync(Network network, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExistsNetworkByNameAsync(string name, Guid? excludeId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Region>> GetRegionsAsync(Guid networkId, bool? activeOnly, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Region?> GetRegionByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Region> CreateRegionAsync(Region region, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Region> UpdateRegionAsync(Region region, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExistsRegionByNameInNetworkAsync(Guid networkId, string name, Guid? excludeId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Establishment>> GetEstablishmentsAsync(Guid regionId, bool? activeOnly, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Establishment?> GetEstablishmentByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Establishment> CreateEstablishmentAsync(Establishment establishment, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Establishment> UpdateEstablishmentAsync(Establishment establishment, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExistsEstablishmentByCodeInRegionAsync(Guid regionId, string code, Guid? excludeId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeAuditTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Append(AuditEntry entry) => Entries.Add(entry);
    }

    private sealed class FakeCurrentUser(Guid networkId) : ICurrentUser
    {
        public Guid UserId { get; } = Guid.NewGuid();

        public string Role => Roles.Analista;

        public bool IsPlatformAdmin => false;

        public Guid NetworkId => networkId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
