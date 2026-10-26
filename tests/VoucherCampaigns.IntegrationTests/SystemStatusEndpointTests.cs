using System.Net;
using System.Net.Http.Json;
using VoucherCampaigns.Application.SystemStatus;

namespace VoucherCampaigns.IntegrationTests;

public class SystemStatusEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task GetStatus_ReturnsProviderConnectionAndAppliedMigrations()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/system/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var status = await response.Content.ReadFromJsonAsync<SystemStatusResponse>();

        Assert.NotNull(status);
        Assert.Equal(factory.Provider, status.Provider);
        Assert.True(status.CanConnect);
        Assert.Single(status.AppliedMigrations, migration => migration.EndsWith("_InitialCreate"));
    }

    [Fact]
    public async Task GetHealth_ReturnsHealthy()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
