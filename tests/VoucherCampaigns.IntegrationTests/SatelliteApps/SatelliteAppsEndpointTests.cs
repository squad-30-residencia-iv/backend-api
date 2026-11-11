using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Identity;
using VoucherCampaigns.Infrastructure.Persistence;
using VoucherCampaigns.Infrastructure.Persistence.Seed;
using VoucherCampaigns.IntegrationTests.Authentication;

namespace VoucherCampaigns.IntegrationTests.SatelliteApps;

public class SatelliteAppsEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string BaseRoute = "/api/v1/satellite/apps";
    private const string TokenRoute = "/api/v1/satellite/token";

    [Fact]
    public async Task Create_AsAdmin_ReturnsCreatedAndExposesSecretOnce()
    {
        var client = factory.CreateClient().WithBearer(TestAuth.CreateToken(factory, Roles.Administrador));
        var request = new CreateSatelliteAppRequest(
            "PDV Loja 1",
            "PDV-LOJA-1",
            DemoDataSeeder.NetworkId,
            ["campaign:read", "voucher:redeem"]);

        var response = await client.PostAsJsonAsync(BaseRoute, request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreateSatelliteAppResponse>();
        Assert.NotNull(created);
        Assert.NotNull(created.ClientSecret);
        Assert.StartsWith("sec_", created.ClientSecret);
        Assert.Equal("PDV Loja 1", created.App.Name);
        Assert.Equal("pdv-loja-1", created.App.ClientId);
        Assert.Equal(DemoDataSeeder.NetworkId, created.App.NetworkId);
        Assert.True(created.App.IsActive);
        Assert.True(created.App.Active);

        // O cabeçalho Location deve apontar para a consulta
        Assert.NotNull(response.Headers.Location);

        // Verifica que no banco o segredo não está em texto claro
        await using var scope = factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().SatelliteApps
            .SingleAsync(a => a.ClientId == "pdv-loja-1");
        Assert.DoesNotContain(created.ClientSecret, stored.SecretHash);

        // Consulta subsequente (GET) NUNCA traz o segredo
        var getResponse = await client.GetAsync($"{BaseRoute}/{created.App.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<SatelliteAppItemResponse>();
        Assert.Equal(created.App.Id, fetched!.Id);
        Assert.Equal("pdv-loja-1", fetched.ClientId);
    }

    [Fact]
    public async Task Create_WithDuplicateClientId_ReturnsUnprocessableEntity422()
    {
        var client = factory.CreateClient().WithBearer(TestAuth.CreateToken(factory, Roles.Administrador));
        var request1 = new CreateSatelliteAppRequest("App 1", "pdv-dup-test", DemoDataSeeder.NetworkId, [SatelliteScopes.ReadCampaigns]);
        var response1 = await client.PostAsJsonAsync(BaseRoute, request1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

        var request2 = new CreateSatelliteAppRequest("App 2", "PDV-DUP-TEST", DemoDataSeeder.NetworkId, [SatelliteScopes.IssueVouchers]);
        var response2 = await client.PostAsJsonAsync(BaseRoute, request2);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response2.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response2);
        Assert.Equal("SATELLITE_CLIENT_ID_TAKEN", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_WithInvalidScope_ReturnsBadRequest400()
    {
        var client = factory.CreateClient().WithBearer(TestAuth.CreateToken(factory, Roles.Administrador));
        var request = new CreateSatelliteAppRequest(
            "App Inv",
            "pdv-inv-scope",
            DemoDataSeeder.NetworkId,
            ["escopo:invalido"]);

        var response = await client.PostAsJsonAsync(BaseRoute, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response);
        Assert.Equal("VALIDATION_ERROR", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_WithEmptyNetwork_ReturnsBadRequest400()
    {
        var client = factory.CreateClient().WithBearer(TestAuth.CreateToken(factory, Roles.Administrador));
        var request = new CreateSatelliteAppRequest(
            "App Empty Net",
            "pdv-empty-net",
            Guid.Empty,
            [SatelliteScopes.ReadCampaigns]);

        var response = await client.PostAsJsonAsync(BaseRoute, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response);
        Assert.Equal("VALIDATION_ERROR", problem.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(Roles.Analista)]
    [InlineData(Roles.Gestor)]
    public async Task Create_AsAnalistaOrGestor_ReturnsForbidden403(string role)
    {
        var client = factory.CreateClient().WithBearer(TestAuth.CreateToken(factory, role));
        var request = new CreateSatelliteAppRequest("App 1", "pdv-forbidden", DemoDataSeeder.NetworkId, [SatelliteScopes.ReadCampaigns]);

        var response = await client.PostAsJsonAsync(BaseRoute, request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response);
        Assert.Equal("AUTH_FORBIDDEN_ROLE", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_Anonymous_ReturnsUnauthorized401()
    {
        var client = factory.CreateClient();
        var request = new CreateSatelliteAppRequest("App 1", "pdv-anon", DemoDataSeeder.NetworkId, [SatelliteScopes.ReadCampaigns]);

        var response = await client.PostAsJsonAsync(BaseRoute, request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetByNetwork_AsAdmin_ReturnsList()
    {
        var client = factory.CreateClient().WithBearer(TestAuth.CreateToken(factory, Roles.Administrador));
        var response = await client.GetAsync($"{BaseRoute}?networkId={DemoDataSeeder.NetworkId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<IReadOnlyList<SatelliteAppItemResponse>>();
        Assert.NotNull(list);
    }

    [Fact]
    public async Task GetByNetwork_WithEmptyNetworkId_ReturnsBadRequest400()
    {
        var client = factory.CreateClient().WithBearer(TestAuth.CreateToken(factory, Roles.Administrador));
        var response = await client.GetAsync($"{BaseRoute}?networkId={Guid.Empty}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await TestAuth.ReadProblemAsync(response);
        Assert.Equal("VALIDATION_ERROR", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Update_AsAdmin_UpdatesNameAndScopes()
    {
        var client = factory.CreateClient().WithBearer(TestAuth.CreateToken(factory, Roles.Administrador));
        var createResponse = await client.PostAsJsonAsync(BaseRoute, new CreateSatelliteAppRequest(
            "Nome Original", "pdv-update-test", DemoDataSeeder.NetworkId, [SatelliteScopes.ReadCampaigns]));
        createResponse.EnsureSuccessStatusCode();
        var created = (await createResponse.Content.ReadFromJsonAsync<CreateSatelliteAppResponse>())!.App;

        var updateResponse = await client.PutAsJsonAsync($"{BaseRoute}/{created.Id}", new UpdateSatelliteAppRequest(
            "Nome Alterado", [SatelliteScopes.IssueVouchers, SatelliteScopes.RedeemVouchers]));

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<SatelliteAppItemResponse>();
        Assert.Equal("Nome Alterado", updated!.Name);
        Assert.Equal("pdv-update-test", updated.ClientId);
        Assert.Equal(DemoDataSeeder.NetworkId, updated.NetworkId);
        Assert.Contains(SatelliteScopes.IssueVouchers, updated.Scopes);
        Assert.Contains(SatelliteScopes.RedeemVouchers, updated.Scopes);
    }

    [Fact]
    public async Task Delete_AsAdmin_DeactivatesAppAndPreventsNewTokens()
    {
        var adminClient = factory.CreateClient().WithBearer(TestAuth.CreateToken(factory, Roles.Administrador));
        var createResponse = await adminClient.PostAsJsonAsync(BaseRoute, new CreateSatelliteAppRequest(
            "App Inativavel", "pdv-deact-test", DemoDataSeeder.NetworkId, [SatelliteScopes.ReadCampaigns]));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreateSatelliteAppResponse>();

        // Autentica com sucesso antes da inativação
        var tokenBefore = await factory.CreateClient().PostAsJsonAsync(TokenRoute, new SatelliteTokenRequest(
            "pdv-deact-test", created!.ClientSecret));
        Assert.Equal(HttpStatusCode.OK, tokenBefore.StatusCode);

        // Inativa a aplicação via DELETE
        var deleteResponse = await adminClient.DeleteAsync($"{BaseRoute}/{created.App.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        var deactivated = await deleteResponse.Content.ReadFromJsonAsync<SatelliteAppItemResponse>();
        Assert.False(deactivated!.IsActive);
        Assert.False(deactivated.Active);

        // Nova tentativa de obter token agora é recusada com 401
        var tokenAfter = await factory.CreateClient().PostAsJsonAsync(TokenRoute, new SatelliteTokenRequest(
            "pdv-deact-test", created.ClientSecret));
        Assert.Equal(HttpStatusCode.Unauthorized, tokenAfter.StatusCode);
    }

    [Fact]
    public async Task DeactivatePatch_AsAdmin_DeactivatesApp()
    {
        var adminClient = factory.CreateClient().WithBearer(TestAuth.CreateToken(factory, Roles.Administrador));
        var createResponse = await adminClient.PostAsJsonAsync(BaseRoute, new CreateSatelliteAppRequest(
            "App Patch Deact", "pdv-patch-deact", DemoDataSeeder.NetworkId, [SatelliteScopes.ReadCampaigns]));
        createResponse.EnsureSuccessStatusCode();
        var created = (await createResponse.Content.ReadFromJsonAsync<CreateSatelliteAppResponse>())!.App;

        var patchResponse = await adminClient.PatchAsync($"{BaseRoute}/{created.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var deactivated = await patchResponse.Content.ReadFromJsonAsync<SatelliteAppItemResponse>();
        Assert.False(deactivated!.IsActive);
        Assert.False(deactivated.Active);
    }

    [Fact]
    public async Task RotateSecret_AsAdmin_RotatesSecretAndInvalidatesOld()
    {
        var adminClient = factory.CreateClient().WithBearer(TestAuth.CreateToken(factory, Roles.Administrador));
        var createResponse = await adminClient.PostAsJsonAsync(BaseRoute, new CreateSatelliteAppRequest(
            "App Rotate Test", "pdv-rotate-test", DemoDataSeeder.NetworkId, [SatelliteScopes.ReadCampaigns]));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreateSatelliteAppResponse>();

        // Rotaciona segredo
        var rotateResponse = await adminClient.PostAsync($"{BaseRoute}/{created!.App.Id}/rotate-secret", null);
        Assert.Equal(HttpStatusCode.OK, rotateResponse.StatusCode);
        var rotated = await rotateResponse.Content.ReadFromJsonAsync<RotateSecretResponse>();
        Assert.NotNull(rotated!.ClientSecret);
        Assert.NotEqual(created.ClientSecret, rotated.ClientSecret);

        // Segredo antigo deixa de autenticar na hora
        var oldTokenResponse = await factory.CreateClient().PostAsJsonAsync(TokenRoute, new SatelliteTokenRequest(
            "pdv-rotate-test", created.ClientSecret));
        Assert.Equal(HttpStatusCode.Unauthorized, oldTokenResponse.StatusCode);

        // Novo segredo autentica normalmente
        var newTokenResponse = await factory.CreateClient().PostAsJsonAsync(TokenRoute, new SatelliteTokenRequest(
            "pdv-rotate-test", rotated.ClientSecret));
        Assert.Equal(HttpStatusCode.OK, newTokenResponse.StatusCode);
    }
}
