using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.Identity;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Infrastructure.Identity;
using VoucherCampaigns.Infrastructure.Persistence;

namespace VoucherCampaigns.UnitTests.Identity;

public class SatelliteAppManagementServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly FixedTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 11, 10, 0, 0, TimeSpan.Zero));
    private readonly SatelliteAppManagementService _service;
    private readonly Guid _networkId = Guid.NewGuid();

    public SatelliteAppManagementServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new SatelliteAppManagementService(_context, _timeProvider);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task CreateAsync_WithValidData_CreatesAppAndReturnsSecretOnce()
    {
        var request = new CreateSatelliteAppRequest(
            "PDV Central",
            "PDV-001",
            _networkId,
            ["campaign:read", "voucher:redeem"]);

        var response = await _service.CreateAsync(request, CancellationToken.None);

        Assert.NotNull(response.ClientSecret);
        Assert.StartsWith("sec_", response.ClientSecret);
        Assert.True(response.ClientSecret.Length >= 32);

        Assert.Equal("PDV Central", response.App.Name);
        Assert.Equal("pdv-001", response.App.ClientId);
        Assert.Equal(_networkId, response.App.NetworkId);
        Assert.True(response.App.IsActive);
        Assert.True(response.App.Active);
        Assert.Contains(SatelliteScopes.ReadCampaigns, response.App.Scopes);
        Assert.Contains(SatelliteScopes.RedeemVouchers, response.App.Scopes);

        var stored = await _context.SatelliteApps.SingleAsync(a => a.ClientId == "pdv-001");
        Assert.DoesNotContain(response.ClientSecret, stored.SecretHash);
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateClientId_ThrowsBusinessRuleException()
    {
        var request1 = new CreateSatelliteAppRequest("App 1", "pdv-duplicate", _networkId, [SatelliteScopes.ReadCampaigns]);
        await _service.CreateAsync(request1, CancellationToken.None);

        var request2 = new CreateSatelliteAppRequest("App 2", "PDV-DUPLICATE", _networkId, [SatelliteScopes.IssueVouchers]);

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _service.CreateAsync(request2, CancellationToken.None));

        Assert.Equal(SatelliteAppManagementService.ClientIdTakenCode, exception.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_WithInvalidName_ThrowsRequestValidationException(string name)
    {
        var request = new CreateSatelliteAppRequest(name, "pdv-01", _networkId, [SatelliteScopes.ReadCampaigns]);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            _service.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_WithEmptyNetworkId_ThrowsRequestValidationException()
    {
        var request = new CreateSatelliteAppRequest("App", "pdv-01", Guid.Empty, [SatelliteScopes.ReadCampaigns]);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            _service.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_WithUnknownScope_ThrowsRequestValidationException()
    {
        var request = new CreateSatelliteAppRequest("App", "pdv-01", _networkId, ["unknown:scope"]);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            _service.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task GetByNetworkAsync_ReturnsOnlyAppsOfGivenNetwork()
    {
        var otherNetwork = Guid.NewGuid();
        await _service.CreateAsync(new("App 1", "app-1", _networkId, [SatelliteScopes.ReadCampaigns]), CancellationToken.None);
        await _service.CreateAsync(new("App 2", "app-2", otherNetwork, [SatelliteScopes.ReadCampaigns]), CancellationToken.None);

        var list = await _service.GetByNetworkAsync(_networkId, CancellationToken.None);

        var single = Assert.Single(list);
        Assert.Equal("app-1", single.ClientId);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesNameAndScopes()
    {
        var created = await _service.CreateAsync(
            new("Nome Antigo", "app-update", _networkId, [SatelliteScopes.ReadCampaigns]), CancellationToken.None);

        var updated = await _service.UpdateAsync(
            created.App.Id,
            new("Nome Novo", [SatelliteScopes.IssueVouchers, SatelliteScopes.RedeemVouchers]),
            CancellationToken.None);

        Assert.Equal("Nome Novo", updated.Name);
        Assert.Equal("app-update", updated.ClientId);
        Assert.Equal(_networkId, updated.NetworkId);
        Assert.DoesNotContain(SatelliteScopes.ReadCampaigns, updated.Scopes);
        Assert.Contains(SatelliteScopes.IssueVouchers, updated.Scopes);
        Assert.Contains(SatelliteScopes.RedeemVouchers, updated.Scopes);
    }

    [Fact]
    public async Task DeactivateAsync_SetsIsActiveFalse()
    {
        var created = await _service.CreateAsync(
            new("App Deactivate", "app-deact", _networkId, [SatelliteScopes.ReadCampaigns]), CancellationToken.None);

        var deactivated = await _service.DeactivateAsync(created.App.Id, CancellationToken.None);

        Assert.False(deactivated.IsActive);
        Assert.False(deactivated.Active);

        var stored = await _context.SatelliteApps.FindAsync(created.App.Id);
        Assert.False(stored!.IsActive);
    }

    [Fact]
    public async Task RotateSecretAsync_GeneratesNewSecretAndUpdatesHash()
    {
        var created = await _service.CreateAsync(
            new("App Rotate", "app-rot", _networkId, [SatelliteScopes.ReadCampaigns]), CancellationToken.None);

        var storedBefore = await _context.SatelliteApps.AsNoTracking().SingleAsync(a => a.Id == created.App.Id);

        var rotated = await _service.RotateSecretAsync(created.App.Id, CancellationToken.None);

        Assert.NotNull(rotated.ClientSecret);
        Assert.NotEqual(created.ClientSecret, rotated.ClientSecret);

        var storedAfter = await _context.SatelliteApps.AsNoTracking().SingleAsync(a => a.Id == created.App.Id);
        Assert.NotEqual(storedBefore.SecretHash, storedAfter.SecretHash);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
