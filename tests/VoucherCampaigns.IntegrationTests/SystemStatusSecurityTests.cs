using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VoucherCampaigns.IntegrationTests;

/// <summary>Task 1: o status do sistema só expõe informações básicas, nunca credenciais.</summary>

public class SystemStatusSecurityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Route = "/api/v1/system/status";

    [Fact]
    public async Task GetStatus_ExposesOnlyProviderConnectionAndMigrations()
    {
        var response = await factory.CreateClient().GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var fields = body.EnumerateObject().Select(property => property.Name).Order().ToArray();

        Assert.Equal(new[] { "appliedMigrations", "canConnect", "provider" }, fields);
    }

    [Fact]
    public async Task GetStatus_DoesNotLeakConnectionStringOrCredentials()
    {
        var response = await factory.CreateClient().GetAsync(Route);
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(factory.ConnectionString, body);

        foreach (var term in new[] { "password", "senha", "username", "host=", "data source", "signingkey", "secret" })
        {
            Assert.DoesNotContain(term, body, StringComparison.OrdinalIgnoreCase);
        }
    }
}
