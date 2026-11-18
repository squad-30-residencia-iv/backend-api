using System.Net;
using System.Net.Http.Json;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.SystemStatus;
using VoucherCampaigns.IntegrationTests.Authentication;

namespace VoucherCampaigns.IntegrationTests;

public class SystemStatusEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task GetStatus_ReturnsProviderConnectionAndAppliedMigrations()
    {
        var client = factory.CreateClient().WithBearer(TestAuth.CreateToken(factory, Roles.Administrador));

        var response = await client.GetAsync("/api/v1/system/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var status = await response.Content.ReadFromJsonAsync<SystemStatusResponse>();

        Assert.NotNull(status);
        Assert.Equal(factory.Provider, status.Provider);
        Assert.True(status.CanConnect);
        Assert.Single(status.AppliedMigrations, migration => migration.EndsWith("_InitialCreate"));
    }

    [Fact]
    public async Task GetStatus_WithoutTokenOrByAnalista_IsRefused()
    {
        // SEC-08 da auditoria (spec GH-066, D-8): provider e migrações só para o administrador.
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/v1/system/status")).StatusCode);

        var analista = factory.CreateClient().WithBearer(TestAuth.CreateToken(factory, Roles.Analista));
        Assert.Equal(HttpStatusCode.Forbidden, (await analista.GetAsync("/api/v1/system/status")).StatusCode);
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
