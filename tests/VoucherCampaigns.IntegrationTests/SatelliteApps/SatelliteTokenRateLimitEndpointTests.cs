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
}
