using Microsoft.IdentityModel.JsonWebTokens;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Infrastructure.Authentication;

namespace VoucherCampaigns.UnitTests.Authentication;

public class JwtAuthServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly AuthOptions _options = AuthOptionsTests.ValidOptions();

    [Fact]
    public void Authenticate_WithValidCredentials_IssuesTokenWithClaims()
    {
        var response = CreateService().Authenticate(new LoginRequest("analista@empresa.com", "Senha123"));

        Assert.NotNull(response);
        Assert.Equal(3600, response.ExpiresIn);
        Assert.Equal(JwtAuthService.SuccessMessage, response.Message);

        var token = new JsonWebToken(response.Token);
        var user = _options.MockUsers[0];

        Assert.Equal(user.UserId, token.Subject);
        Assert.Equal(Roles.Analista, token.GetClaim(AuthClaimTypes.Role).Value);
        Assert.Equal(user.NetworkId, token.GetClaim(AuthClaimTypes.NetworkId).Value);
        Assert.Equal(_options.Issuer, token.Issuer);
        Assert.Equal(_options.Audience, Assert.Single(token.Audiences));
        Assert.Equal(Now.UtcDateTime.AddMinutes(60), token.ValidTo);
    }

    [Fact]
    public void Authenticate_IgnoresEmailCase()
    {
        var response = CreateService().Authenticate(new LoginRequest(" Analista@Empresa.COM ", "Senha123"));

        Assert.NotNull(response);
    }

    [Theory]
    [InlineData("analista@empresa.com", "senha123")]
    [InlineData("analista@empresa.com", "")]
    [InlineData("ninguem@empresa.com", "Senha123")]
    public void Authenticate_WithInvalidCredentials_ReturnsNull(string email, string password)
    {
        var response = CreateService().Authenticate(new LoginRequest(email, password));

        Assert.Null(response);
    }

    private JwtAuthService CreateService() =>
        new(_options, new ConfigurationUserCredentialStore(_options), new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
