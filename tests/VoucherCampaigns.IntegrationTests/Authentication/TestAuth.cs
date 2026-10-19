using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Infrastructure.Authentication;
using VoucherCampaigns.Infrastructure.Persistence.Seed;

namespace VoucherCampaigns.IntegrationTests.Authentication;

public static class TestAuth
{
    public const string AnalistaEmail = "analista@empresa.com";
    public const string GestorEmail = "gestor@empresa.com";
    public const string Password = "Senha123";

    public static readonly Guid OtherNetworkId = Guid.Parse("0199e3a0-0000-7000-8000-0000000000ff");

    public static async Task<string> LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();

        return token!.Token;
    }

    /// <summary>Monta um token como o da Api, permitindo variar papel, rede, validade e chave.</summary>
    public static string CreateToken(
        ApiFactory factory,
        string role = Roles.Analista,
        Guid? networkId = null,
        DateTime? expires = null,
        string? signingKey = null)
    {
        var options = factory.Services.GetRequiredService<AuthOptions>();
        var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(10);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = expiresAt.AddMinutes(-20),
            NotBefore = expiresAt.AddMinutes(-20),
            Expires = expiresAt,
            Claims = new Dictionary<string, object>
            {
                [AuthClaimTypes.UserId] = Guid.NewGuid().ToString(),
                [AuthClaimTypes.Role] = role,
                [AuthClaimTypes.NetworkId] = (networkId ?? DemoDataSeeder.NetworkId).ToString()
            },
            SigningCredentials = new SigningCredentials(
                JwtAuthService.CreateSigningKey(options with { SigningKey = signingKey ?? options.SigningKey }),
                SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public static HttpClient WithBearer(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
