using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using VoucherCampaigns.Application.Authentication;

namespace VoucherCampaigns.IntegrationTests.Authentication;

/// <summary>Task 2: as respostas de login não expõem senha, hash nem dados além do token.</summary>
public class LoginResponseSecurityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Campos permitidos na resposta do login. "profile" foi adicionado pela US-02.5 (PR #39);
    // o teste só exige que nada fora desta lista (senha, hash etc.) apareça, valendo antes e depois dele.
    private static readonly string[] AllowedResponseFields = new[] { "token", "expiresIn", "message", "profile" };
    private static readonly string[] AllowedProfileFields = new[] { "userId", "email", "role", "networkId" };

    [Theory]
    [InlineData(TestAuth.AnalistaEmail)]
    [InlineData(TestAuth.GestorEmail)]
    public async Task Login_Success_ReturnsOnlyAllowedFieldsWithoutSensitiveData(string email)
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, TestAuth.Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(raw).RootElement;

        Assert.All(body.EnumerateObject(), property => Assert.Contains(property.Name, AllowedResponseFields));

        if (body.TryGetProperty("profile", out var profile) && profile.ValueKind == JsonValueKind.Object)
        {
            Assert.All(profile.EnumerateObject(), property => Assert.Contains(property.Name, AllowedProfileFields));
        }

        Assert.DoesNotContain(TestAuth.Password, raw);
    }

    [Fact]
    public async Task Login_Token_DoesNotCarryThePassword()
    {
        var token = new JsonWebToken(await TestAuth.LoginAsync(factory.CreateClient(), TestAuth.AnalistaEmail));

        Assert.DoesNotContain(token.Claims, claim => claim.Value.Contains(TestAuth.Password, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(token.Claims, claim => claim.Type.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(TestAuth.AnalistaEmail, "senha-errada")]
    [InlineData("ninguem@empresa.com", "outra-senha-qualquer")]
    public async Task Login_Failure_DoesNotEchoCredentialsAndDoesNotRevealWhichOneFailed(string email, string password)
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(password, raw);
        Assert.DoesNotContain(email, raw);
        Assert.Contains("AUTH_INVALID_CREDENTIALS", raw);
    }
}
