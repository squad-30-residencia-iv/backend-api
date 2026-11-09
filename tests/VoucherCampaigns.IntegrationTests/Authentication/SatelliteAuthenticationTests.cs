using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Domain.Identity;
using VoucherCampaigns.Infrastructure.Authentication;
using VoucherCampaigns.Infrastructure.Persistence;
using VoucherCampaigns.Infrastructure.Persistence.Seed;

namespace VoucherCampaigns.IntegrationTests.Authentication;

public class SatelliteAuthenticationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string TokenRoute = "/api/v1/satellite/token";
    private const string MeRoute = "/api/v1/satellite/me";

    [Theory]
    [InlineData(TestAuth.SatelliteClientId)]
    [InlineData(" PDV-Demo ")]
    public async Task Token_WithValidCredentials_ReturnsBearerThatOpensMe(string clientId)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            TokenRoute, new SatelliteTokenRequest(clientId, TestAuth.SatelliteSecret));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<SatelliteTokenResponse>();
        Assert.Equal("Bearer", token!.TokenType);
        Assert.Equal(3600, token.ExpiresIn);
        Assert.Equal(SatelliteScopes.All, token.Scopes);

        var me = await factory.CreateClient().WithBearer(token.AccessToken).GetFromJsonAsync<SatelliteAppResponse>(MeRoute);

        await using var scope = factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().SatelliteApps
            .SingleAsync(app => app.ClientId == TestAuth.SatelliteClientId);
        Assert.Equal(stored.Id, me!.ApplicationId);
        Assert.Equal(TestAuth.SatelliteClientId, me.ClientId);
        Assert.Equal(DemoDataSeeder.NetworkId, me.NetworkId);
        Assert.Equal(SatelliteScopes.All, me.Scopes);
    }

    [Fact]
    public async Task Seed_StoresOnlyTheSecretHash()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().SatelliteApps
            .SingleAsync(app => app.ClientId == TestAuth.SatelliteClientId);

        Assert.DoesNotContain(TestAuth.SatelliteSecret, stored.SecretHash);
        Assert.True(PasswordHasher.Verify(TestAuth.SatelliteSecret, stored.SecretHash));
    }

    [Theory]
    [InlineData(TestAuth.SatelliteClientId, "segredo-errado-com-mais-de-trinta-e-dois-caracteres")]
    [InlineData("pdv-inexistente", TestAuth.SatelliteSecret)]
    public async Task Token_WithInvalidCredentials_ReturnsInvalidClient(string clientId, string secret)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(TokenRoute, new SatelliteTokenRequest(clientId, secret));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "AUTH_INVALID_CLIENT", "Credenciais da aplicação inválidas.");
    }

    [Fact]
    public async Task Token_ForInactiveApp_ReturnsInvalidClient()
    {
        const string secret = "segredo-do-app-inativo-com-mais-de-32-caracteres";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.SatelliteApps.Add(new SatelliteApp
            {
                Id = Guid.CreateVersion7(),
                ClientId = "app-inativo",
                Name = "App inativo",
                SecretHash = PasswordHasher.Hash(secret),
                NetworkId = DemoDataSeeder.NetworkId,
                Scopes = SatelliteScopes.IssueVouchers,
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var response = await factory.CreateClient().PostAsJsonAsync(TokenRoute, new SatelliteTokenRequest("app-inativo", secret));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "AUTH_INVALID_CLIENT", "Credenciais da aplicação inválidas.");
    }

    [Fact]
    public async Task Token_WithoutSecret_ReturnsValidationProblem()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(TokenRoute, new { clientId = TestAuth.SatelliteClientId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response);
        Assert.Equal("VALIDATION_ERROR", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthenticated()
    {
        var response = await factory.CreateClient().GetAsync(MeRoute);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "AUTH_UNAUTHENTICATED", "Autenticação necessária.");
    }

    [Fact]
    public async Task Me_WithUserToken_ReturnsUnauthenticated()
    {
        var response = await factory.CreateClient().WithBearer(TestAuth.CreateToken(factory, Roles.Gestor)).GetAsync(MeRoute);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "AUTH_UNAUTHENTICATED", "Autenticação necessária.");
    }

    [Fact]
    public async Task Me_WithExpiredOrForeignToken_ReturnsUnauthenticated()
    {
        var expired = TestAuth.CreateSatelliteToken(factory, expires: DateTime.UtcNow.AddMinutes(-5));
        var foreign = TestAuth.CreateSatelliteToken(factory, signingKey: new string('x', 40));

        var expiredResponse = await factory.CreateClient().WithBearer(expired).GetAsync(MeRoute);
        var foreignResponse = await factory.CreateClient().WithBearer(foreign).GetAsync(MeRoute);

        await AssertProblemAsync(expiredResponse, HttpStatusCode.Unauthorized, "AUTH_UNAUTHENTICATED", "Autenticação necessária.");
        await AssertProblemAsync(foreignResponse, HttpStatusCode.Unauthorized, "AUTH_UNAUTHENTICATED", "Autenticação necessária.");
    }

    [Fact]
    public async Task UserRoute_WithSatelliteToken_ReturnsUnauthenticated()
    {
        var client = factory.CreateClient().WithBearer(TestAuth.CreateSatelliteToken(factory));

        var response = await client.GetAsync($"/api/v1/campaigns?networkId={DemoDataSeeder.NetworkId}");

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "AUTH_UNAUTHENTICATED", "Autenticação necessária.");
    }

    [Fact]
    public async Task ScopedRoute_WithIssuedToken_IsAllowed()
    {
        var token = await IssueTokenAsync();

        var response = await factory.CreateClient().WithBearer(token).PostAsync("/test/satellite/issue", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ScopedRoute_WithoutTheScope_ReturnsForbiddenScope()
    {
        var token = TestAuth.CreateSatelliteToken(factory, scopes: [SatelliteScopes.RedeemVouchers]);

        var response = await factory.CreateClient().WithBearer(token).PostAsync("/test/satellite/issue", null);

        await AssertProblemAsync(
            response, HttpStatusCode.Forbidden, "AUTH_FORBIDDEN_SCOPE", "A aplicação não tem permissão para esta operação.");
    }

    [Fact]
    public async Task NetworkRoute_UsesTheNetworkOfTheApp()
    {
        var client = factory.CreateClient().WithBearer(TestAuth.CreateSatelliteToken(factory));

        var own = await client.GetAsync($"/test/satellite/networks/{DemoDataSeeder.NetworkId}");
        var other = await client.GetAsync($"/test/satellite/networks/{TestAuth.OtherNetworkId}");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        await AssertProblemAsync(
            other, HttpStatusCode.Forbidden, "SCOPE_FORBIDDEN", "Escopo organizacional fora da sua permissão de acesso");
    }

    private async Task<string> IssueTokenAsync()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            TokenRoute, new SatelliteTokenRequest(TestAuth.SatelliteClientId, TestAuth.SatelliteSecret));
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<SatelliteTokenResponse>())!.AccessToken;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code, string detail)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal(detail, problem.GetProperty("detail").GetString());
    }
}
