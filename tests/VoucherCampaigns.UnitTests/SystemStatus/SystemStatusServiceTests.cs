using VoucherCampaigns.Application.SystemStatus;

namespace VoucherCampaigns.UnitTests.SystemStatus;

public class SystemStatusServiceTests
{
    [Fact]
    public async Task GetStatusAsync_ReturnsStatusProvidedByDatabaseStatusReader()
    {
        var expected = new SystemStatusResponse("Sqlite", true, ["20260101000000_InitialCreate"]);
        var service = new SystemStatusService(new FakeDatabaseStatusReader(expected));

        var result = await service.GetStatusAsync(CancellationToken.None);

        Assert.Same(expected, result);
    }

    private sealed class FakeDatabaseStatusReader(SystemStatusResponse response) : IDatabaseStatusReader
    {
        public Task<SystemStatusResponse> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
