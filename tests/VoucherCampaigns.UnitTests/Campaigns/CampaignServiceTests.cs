using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Application.Common;
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
    public async Task GetByIdAsync_ForCampaignOfAnotherNetwork_ThrowsScopeForbidden()
    {
        var other = new Network { Name = "Outra rede", TimeZone = "America/Sao_Paulo" };
        _campaigns.Added.Add(new Campaign { NetworkId = other.Id, Network = other, Name = "Fora do escopo" });

        await Assert.ThrowsAsync<ScopeForbiddenException>(() =>
            CreateService(Network).GetByIdAsync(_campaigns.Added[0].Id, CancellationToken.None));
    }

    private CampaignService CreateService(Network? network) =>
        new(new FakeNetworkRepository(network), _campaigns, new FakeCurrentUser(Network.Id), new FixedTimeProvider(Now));

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

        public Task<IReadOnlyList<Campaign>> GetByNetworkAsync(Guid networkId, CampaignStatus? status, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Campaign>>(
                Added.Where(campaign => campaign.NetworkId == networkId && (status is null || campaign.Status == status)).ToList());

        public Task<Campaign?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Added.FirstOrDefault(campaign => campaign.Id == id));
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
