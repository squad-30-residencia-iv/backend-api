using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Infrastructure.Persistence.Seed;

namespace VoucherCampaigns.IntegrationTests.Authentication;

public class ProtectedRouteTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ProtectedRoute_WithoutToken_ReturnsUnauthenticatedProblem()
    {
        var response = await factory.CreateClient().GetAsync("/test/auth/me");

        await AssertUnauthenticatedAsync(response);
    }

    [Fact]
    public async Task ProtectedRoute_WithMalformedToken_ReturnsUnauthenticatedProblem()
    {
        var response = await factory.CreateClient().WithBearer("nao-e-um-jwt").GetAsync("/test/auth/me");

        await AssertUnauthenticatedAsync(response);
    }

    [Fact]
    public async Task ProtectedRoute_WithTokenSignedByAnotherKey_ReturnsUnauthenticatedProblem()
    {
        var token = TestAuth.CreateToken(factory, signingKey: "outra-chave-com-mais-de-trinta-e-dois-caracteres");

        var response = await factory.CreateClient().WithBearer(token).GetAsync("/test/auth/me");

        await AssertUnauthenticatedAsync(response);
    }

    [Fact]
    public async Task ProtectedRoute_WithExpiredToken_ReturnsUnauthenticatedProblem()
    {
        var token = TestAuth.CreateToken(factory, expires: DateTime.UtcNow.AddMinutes(-5));

        var response = await factory.CreateClient().WithBearer(token).GetAsync("/test/auth/me");

        await AssertUnauthenticatedAsync(response);
    }

    [Fact]
    public async Task ProtectedRoute_WithLoginToken_ReturnsCurrentUser()
    {
        var client = factory.CreateClient();
        client.WithBearer(await TestAuth.LoginAsync(client, TestAuth.AnalistaEmail));

        var response = await client.GetAsync("/test/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(Roles.Analista, user.GetProperty("role").GetString());
        Assert.Equal(DemoDataSeeder.NetworkId, user.GetProperty("networkId").GetGuid());
    }

    [Theory]
    [InlineData(TestAuth.AnalistaEmail)]
    [InlineData(TestAuth.GestorEmail)]
    public async Task ManagePolicy_AcceptsAnalistaAndGestor(string email)
    {
        var client = factory.CreateClient();
        client.WithBearer(await TestAuth.LoginAsync(client, email));

        var response = await client.GetAsync("/test/auth/manage");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApprovePolicy_WithAnalista_ReturnsForbiddenRoleProblem()
    {
        var client = factory.CreateClient();
        client.WithBearer(await TestAuth.LoginAsync(client, TestAuth.AnalistaEmail));

        var response = await client.GetAsync("/test/auth/approve");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var problem = await TestAuth.ReadProblemAsync(response);

        Assert.Equal("AUTH_FORBIDDEN_ROLE", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ApprovePolicy_WithGestor_ReturnsOk()
    {
        var client = factory.CreateClient();
        client.WithBearer(await TestAuth.LoginAsync(client, TestAuth.GestorEmail));

        var response = await client.GetAsync("/test/auth/approve");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TenantGuard_WithUserNetworkInRoute_ReturnsOk()
    {
        var client = factory.CreateClient().WithBearer(TestAuth.CreateToken(factory));

        var response = await client.GetAsync($"/test/auth/networks/{DemoDataSeeder.NetworkId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TenantGuard_WithOtherNetworkInRoute_ReturnsScopeForbiddenProblem()
    {
        var client = factory.CreateClient().WithBearer(TestAuth.CreateToken(factory));

        var response = await client.GetAsync($"/test/auth/networks/{TestAuth.OtherNetworkId}");

        await AssertScopeForbiddenAsync(response);
    }

    [Fact]
    public async Task TenantGuard_WithUserNetworkInBody_ReturnsOk()
    {
        var client = factory.CreateClient().WithBearer(TestAuth.CreateToken(factory));

        var response = await client.PostAsJsonAsync("/test/auth/scoped", new ScopedTestRequest(DemoDataSeeder.NetworkId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TenantGuard_WithOtherNetworkInBody_ReturnsScopeForbiddenProblem()
    {
        var client = factory.CreateClient().WithBearer(TestAuth.CreateToken(factory, role: Roles.Gestor));

        var response = await client.PostAsJsonAsync("/test/auth/scoped", new ScopedTestRequest(TestAuth.OtherNetworkId));

        await AssertScopeForbiddenAsync(response);
    }

    [Fact]
    public async Task TenantGuard_OnActionWithoutNetwork_FailsInsteadOfAllowing()
    {
        var client = factory.CreateClient().WithBearer(TestAuth.CreateToken(factory));

        var response = await client.GetAsync("/test/auth/without-network");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/api/v1/system/status")]
    public async Task PublicRoutes_WithoutToken_RemainAccessible(string route)
    {
        var response = await factory.CreateClient().GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertUnauthenticatedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");

        var problem = await TestAuth.ReadProblemAsync(response);

        Assert.Equal("AUTH_UNAUTHENTICATED", problem.GetProperty("code").GetString());
    }

    private static async Task AssertScopeForbiddenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var problem = await TestAuth.ReadProblemAsync(response);

        Assert.Equal("SCOPE_FORBIDDEN", problem.GetProperty("code").GetString());
        Assert.Equal("Escopo organizacional fora da sua permissão de acesso", problem.GetProperty("detail").GetString());
    }
}
