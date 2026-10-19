using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Infrastructure.Persistence.Seed;

namespace VoucherCampaigns.IntegrationTests.Authentication;

public class LoginEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData(TestAuth.AnalistaEmail, Roles.Analista)]
    [InlineData(TestAuth.GestorEmail, Roles.Gestor)]
    public async Task Login_WithValidCredentials_ReturnsTokenWithUserRoleAndNetwork(string email, string role)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, TestAuth.Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();

        Assert.NotNull(body);
        Assert.Equal(3600, body.ExpiresIn);

        var token = new JsonWebToken(body.Token);

        Assert.True(Guid.TryParse(token.Subject, out _));
        Assert.Equal(role, token.GetClaim(AuthClaimTypes.Role).Value);
        Assert.Equal(DemoDataSeeder.NetworkId.ToString(), token.GetClaim(AuthClaimTypes.NetworkId).Value);
    }

    [Theory]
    [InlineData(TestAuth.AnalistaEmail, "senha-errada")]
    [InlineData("ninguem@empresa.com", TestAuth.Password)]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorizedProblem(string email, string password)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await TestAuth.ReadProblemAsync(response);

        Assert.Equal("AUTH_INVALID_CREDENTIALS", problem.GetProperty("code").GetString());
        Assert.Equal("Credenciais inválidas.", problem.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("{ \"password\": \"Senha123\" }", "Email")]
    [InlineData("{ \"email\": \"nao-e-email\", \"password\": \"Senha123\" }", "Email")]
    [InlineData("{ \"email\": \"analista@empresa.com\" }", "Password")]
    public async Task Login_WithInvalidBody_ReturnsValidationProblem(string json, string field)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/api/v1/auth/login", new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await TestAuth.ReadProblemAsync(response);

        Assert.Equal("VALIDATION_ERROR", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }
}
