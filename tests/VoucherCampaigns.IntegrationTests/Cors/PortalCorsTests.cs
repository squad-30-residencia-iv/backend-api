using System.Net.Http.Headers;

namespace VoucherCampaigns.IntegrationTests.Cors;

public class PortalCorsTests
{
    private const string PortalOrigin = "http://localhost:5173";

    [Fact]
    public async Task Login_FromConfiguredOrigin_AllowsThatOrigin()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/login", PortalOrigin);

        Assert.Equal(PortalOrigin, AllowOrigin(response));
    }

    [Fact]
    public async Task Preflight_FromConfiguredOrigin_AllowsLoginHeaders()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        request.Headers.Add("Origin", PortalOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        using var response = await client.SendAsync(request);

        Assert.Equal(PortalOrigin, AllowOrigin(response));
        Assert.Contains("authorization", AllowHeader(response, "Access-Control-Allow-Headers"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("POST", AllowHeader(response, "Access-Control-Allow-Methods"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_FromAnotherOrigin_DoesNotAllowIt()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, HttpMethod.Get, "/health", "https://outro.exemplo");

        Assert.Null(AllowOrigin(response));
    }

    [Theory]
    [InlineData("localhost:5173")]
    [InlineData("http://localhost:5173/portal")]
    [InlineData("http://localhost:5173/")]
    public void Startup_WithInvalidOrigin_Fails(string origin)
    {
        var connectionString = ApiFactory.NewSqliteConnectionString();

        try
        {
            using var factory = ApiFactory.For(
                "Sqlite",
                connectionString,
                settings: new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = origin });

            var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

            Assert.Contains("Configuração inválida", exception.Message);
            Assert.Contains("Cors:AllowedOrigins:0", exception.Message);
        }
        finally
        {
            ApiFactory.DeleteSqliteDatabase(connectionString);
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string origin)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Origin", origin);
        if (method == HttpMethod.Post)
        {
            request.Content = new StringContent("""{"email":"analista@empresa.com","password":"Senha123"}""");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        return await client.SendAsync(request);
    }

    private static string? AllowOrigin(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.Single() : null;

    private static string AllowHeader(HttpResponseMessage response, string name) =>
        string.Join(',', response.Headers.GetValues(name));
}
