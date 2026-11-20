using System.Net;
using System.Net.Http.Json;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.IntegrationTests.Authentication;

namespace VoucherCampaigns.IntegrationTests.SatelliteApps;

public class SatelliteTokenRateLimitEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string TokenRoute = "/api/v1/satellite/token";

    [Fact]
    public async Task RepeatedFailedAttempts_ExceedingLimit_Returns429TooManyRequests()
    {
        const string clientId = "pdv-brute-force-target";
        const string wrongSecret = "segredo-errado-12345678901234567890123456789012";

        var client = factory.CreateClient();

        // As primeiras 5 tentativas inválidas respondem 401
        for (var i = 0; i < 5; i++)
        {
            var response = await client.PostAsJsonAsync(TokenRoute, new SatelliteTokenRequest(clientId, wrongSecret));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // A 6ª tentativa excede o limite e responde 429 Too Many Requests
        var blockedResponse = await client.PostAsJsonAsync(TokenRoute, new SatelliteTokenRequest(clientId, wrongSecret));
        Assert.Equal(HttpStatusCode.TooManyRequests, blockedResponse.StatusCode);

        var problem = await TestAuth.ReadProblemAsync(blockedResponse);
        Assert.Equal("SATELLITE_TOKEN_RATE_LIMIT_EXCEEDED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TokenRequests_AboveThePerIpLimit_Return429WithRetryAfter()
    {
        // SEC-05 (spec GH-071, D-2): limite por origem, além do bloqueio por clientId. Fábrica própria com limite baixo.
        var connectionString = ApiFactory.NewSqliteConnectionString();
        await using var limited = ApiFactory.For("Sqlite", connectionString, settings: new Dictionary<string, string?>
        {
            ["Auth:Satellite:TokenRateLimit:PermitLimit"] = "3",
            ["Auth:Satellite:TokenRateLimit:WindowSeconds"] = "60"
        });
        var client = limited.CreateClient();

        // clientIds diferentes: o bloqueio por clientId (5 erros) não entra na conta.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var allowed = await client.PostAsJsonAsync(TokenRoute, new SatelliteTokenRequest($"pdv-ip-{attempt}", "segredo-errado"));
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        var refused = await client.PostAsJsonAsync(TokenRoute, new SatelliteTokenRequest("pdv-ip-x", "segredo-errado"));

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("SATELLITE_TOKEN_RATE_LIMITED", (await TestAuth.ReadProblemAsync(refused)).GetProperty("code").GetString());
        Assert.True(refused.Headers.RetryAfter is not null, "sem Retry-After");

        await limited.DisposeAsync();
        ApiFactory.DeleteSqliteDatabase(connectionString);
    }
}
