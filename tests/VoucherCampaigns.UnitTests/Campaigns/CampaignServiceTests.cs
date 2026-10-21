using VoucherCampaigns.Application.Campaigns;
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

    private CampaignService CreateService(Network? network) =>
        new(new FakeNetworkRepository(network), _campaigns, new FixedTimeProvider(Now));

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
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
