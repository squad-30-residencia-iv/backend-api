using System.Net;
using System.Net.Http.Json;

namespace VoucherCampaigns.IntegrationTests.Authentication;

/// <summary>Limite de tentativas no login (spec GH-066, D-6; SEC-01 da auditoria).</summary>
public class LoginRateLimitTests
{
    [Fact]
    public async Task Login_AboveTheLimitInTheWindow_Returns429WithRetryAfter()
    {
        // SQLite em arquivo próprio: o limite vale para a fábrica inteira e não pode ser o dos outros testes.
        var connectionString = ApiFactory.NewSqliteConnectionString();
        await using var factory = ApiFactory.For("Sqlite", connectionString, settings: new Dictionary<string, string?>
        {
            ["Auth:LoginRateLimit:PermitLimit"] = "3",
            ["Auth:LoginRateLimit:WindowSeconds"] = "60"
        });
        var client = factory.CreateClient();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var allowed = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "analista@empresa.com", password = "errada" });
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        var refused = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "analista@empresa.com", password = "errada" });

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("AUTH_LOGIN_RATE_LIMITED", (await TestAuth.ReadProblemAsync(refused)).GetProperty("code").GetString());
        Assert.True(refused.Headers.RetryAfter is not null, "sem Retry-After");

        await factory.DisposeAsync();
        ApiFactory.DeleteSqliteDatabase(connectionString);
    }
}
