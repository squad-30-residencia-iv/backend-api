using System.Net;
using System.Net.Http.Headers;

namespace VoucherCampaigns.IntegrationTests.Authentication;

/// <summary>Task 2: tokens inválidos ou esquemas diferentes de Bearer são rejeitados com 401.</summary>
public class TokenValidationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ProtectedRoute_WithTamperedSignature_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();
        var token = await TestAuth.LoginAsync(client, TestAuth.GestorEmail);

        // Troca um caractere da assinatura (penúltimo, que sempre altera os bytes decodificados).
        var tampered = token[..^2] + (token[^2] == 'A' ? 'B' : 'A') + token[^1];

        var response = await client.WithBearer(tampered).GetAsync("/test/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedRoute_WithNonBearerScheme_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();
        var token = await TestAuth.LoginAsync(client, TestAuth.AnalistaEmail);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);

        var response = await client.GetAsync("/test/auth/manage");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/campaigns")]
    [InlineData("/api/v1/networks")]
    public async Task RealProtectedRoutes_WithoutToken_ReturnUnauthorized(string route)
    {
        var response = await factory.CreateClient().GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
