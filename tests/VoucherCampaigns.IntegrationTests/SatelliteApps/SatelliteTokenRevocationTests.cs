using System.Net;
using System.Net.Http.Json;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Identity;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;

namespace VoucherCampaigns.IntegrationTests.SatelliteApps;

/// <summary>
/// SEC-03 (spec GH-071, D-1): o token do satélite deixa de valer assim que a aplicação é inativada, troca o segredo
/// ou muda os escopos, sem esperar a expiração. Responde 401 como um token vencido.
/// </summary>
public class SatelliteTokenRevocationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AppsRoute = "/api/v1/satellite/apps";
    private const string TokenRoute = "/api/v1/satellite/token";
    private const string MeRoute = "/api/v1/satellite/me";

    [Theory]
    [InlineData("PATCH", "/deactivate")]
    [InlineData("DELETE", "")]
    public async Task DeactivatingTheApp_RevokesTheTokenAtOnce(string method, string suffix)
    {
        var (app, secret) = await CreateAppAsync();
        var token = await TokenAsync(app.ClientId, secret);
        Assert.Equal(HttpStatusCode.OK, (await Me(token)).StatusCode);

        (await Admin().SendAsync(new HttpRequestMessage(new HttpMethod(method), $"{AppsRoute}/{app.Id}{suffix}"))).EnsureSuccessStatusCode();

        await AssertRevokedAsync(token);
    }

    [Fact]
    public async Task RotatingTheSecret_RevokesTheOldToken_AndTheNewSecretWorks()
    {
        var (app, secret) = await CreateAppAsync();
        var oldToken = await TokenAsync(app.ClientId, secret);

        var rotated = await Admin().PostAsync($"{AppsRoute}/{app.Id}/rotate-secret", null);
        rotated.EnsureSuccessStatusCode();
        var newSecret = (await rotated.Content.ReadFromJsonAsync<RotateSecretResponse>())!.ClientSecret;

        await AssertRevokedAsync(oldToken);
        Assert.Equal(HttpStatusCode.OK, (await Me(await TokenAsync(app.ClientId, newSecret))).StatusCode);
    }

    [Fact]
    public async Task ChangingTheScopes_RevokesTheToken_ButRenamingDoesNot()
    {
        var (app, secret) = await CreateAppAsync();
        var token = await TokenAsync(app.ClientId, secret);

        (await Admin().PutAsJsonAsync($"{AppsRoute}/{app.Id}", new UpdateSatelliteAppRequest("Outro nome", [SatelliteScopes.ReadCampaigns])))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await Me(token)).StatusCode);

        (await Admin().PutAsJsonAsync($"{AppsRoute}/{app.Id}", new UpdateSatelliteAppRequest("Outro nome", [SatelliteScopes.IssueVouchers])))
            .EnsureSuccessStatusCode();
        await AssertRevokedAsync(token);
    }

    [Fact]
    public async Task TokenWithoutTheStamp_IsRejected()
    {
        var withStamp = TestAuth.CreateSatelliteToken(factory);
        var withoutStamp = TestAuth.CreateSatelliteToken(factory, includeStamp: false);

        Assert.Equal(HttpStatusCode.OK, (await Me(withStamp)).StatusCode);
        await AssertRevokedAsync(withoutStamp);
    }

    private HttpClient Admin() => factory.CreateClient().WithBearer(TestAuth.CreateToken(factory, Roles.Administrador));

    private Task<HttpResponseMessage> Me(string token) => factory.CreateClient().WithBearer(token).GetAsync(MeRoute);

    private async Task<(SatelliteAppItemResponse App, string Secret)> CreateAppAsync()
    {
        var clientId = $"pdv-{Guid.NewGuid():N}"[..20];
        var response = await Admin().PostAsJsonAsync(AppsRoute, new CreateSatelliteAppRequest(
            "PDV revogação", clientId, DemoDataSeeder.NetworkId, [SatelliteScopes.ReadCampaigns]));
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CreateSatelliteAppResponse>())!;

        return (created.App, created.ClientSecret);
    }

    private async Task<string> TokenAsync(string clientId, string secret)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(TokenRoute, new SatelliteTokenRequest(clientId, secret));
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<SatelliteTokenResponse>())!.AccessToken;
    }

    private async Task AssertRevokedAsync(string token)
    {
        var response = await Me(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("AUTH_UNAUTHENTICATED", (await TestAuth.ReadProblemAsync(response)).GetProperty("code").GetString());
    }
}
