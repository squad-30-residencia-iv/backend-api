using Microsoft.IdentityModel.JsonWebTokens;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Infrastructure.Authentication;

namespace VoucherCampaigns.UnitTests.Authentication;

public class JwtAuthServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly AuthOptions _options = AuthOptionsTests.ValidOptions();

    [Fact]
    public async Task Authenticate_WithValidCredentials_IssuesTokenWithClaims()
    {
        var response = await CreateService().AuthenticateAsync(
            new LoginRequest("analista@empresa.com", "Senha123"), CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(3600, response.ExpiresIn);
        Assert.Equal(JwtAuthService.SuccessMessage, response.Message);

        var token = new JsonWebToken(response.Token);
        var user = _options.MockUsers[0];

        Assert.Equal(user.UserId, token.Subject);
        Assert.Equal(user.Name, response.Profile!.Name);
        Assert.False(token.TryGetClaim("name", out _));
        Assert.Equal(Roles.Analista, token.GetClaim(AuthClaimTypes.Role).Value);
        Assert.Equal(user.NetworkId, token.GetClaim(AuthClaimTypes.NetworkId).Value);
        Assert.Equal(_options.Issuer, token.Issuer);
        Assert.Equal(_options.Audience, Assert.Single(token.Audiences));
        Assert.Equal(Now.UtcDateTime.AddMinutes(60), token.ValidTo);
    }

    [Fact]
    public async Task Authenticate_AsAdministrador_IssuesTokenWithoutNetwork()
    {
        var admin = AuthOptionsTests.ValidUser("admin@empresa.com", Roles.Administrador) with
        {
            NetworkId = "",
            Name = "Admin Plataforma"
        };
        var options = _options with { MockUsers = [admin] };
        var service = new JwtAuthService(options, new FakeUserCredentialStore(options), new FixedTimeProvider(Now));

        var response = await service.AuthenticateAsync(
            new LoginRequest("admin@empresa.com", "Senha123"), CancellationToken.None);

        Assert.NotNull(response);
        var token = new JsonWebToken(response.Token);
        Assert.Equal("Admin Plataforma", response.Profile!.Name);
        Assert.Equal(Roles.Administrador, token.GetClaim(AuthClaimTypes.Role).Value);
        Assert.False(token.TryGetClaim(AuthClaimTypes.NetworkId, out _));
    }

    [Fact]
    public async Task Authenticate_IgnoresEmailCase()
    {
        var response = await CreateService().AuthenticateAsync(
            new LoginRequest(" Analista@Empresa.COM ", "Senha123"), CancellationToken.None);

        Assert.NotNull(response);
    }

    [Theory]
    [InlineData("analista@empresa.com", "senha123")]
    [InlineData("analista@empresa.com", "")]
    [InlineData("ninguem@empresa.com", "Senha123")]
    public async Task Authenticate_WithInvalidCredentials_ReturnsNull(string email, string password)
    {
        var response = await CreateService().AuthenticateAsync(new LoginRequest(email, password), CancellationToken.None);

        Assert.Null(response);
    }

    private JwtAuthService CreateService() =>
        new(_options, new FakeUserCredentialStore(_options), new FixedTimeProvider(Now));

    /// <summary>Imita o store do banco sem abrir uma conexão: e-mail sem caixa e senha exata.</summary>
    private sealed class FakeUserCredentialStore(AuthOptions options) : IUserCredentialStore
    {
        public Task<UserAccount?> FindByCredentialsAsync(
            string email, string password, CancellationToken cancellationToken)
        {
            var user = options.MockUsers.FirstOrDefault(candidate =>
                string.Equals(candidate.Email.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));

            UserAccount? account = user is not null && user.Password == password ? user.ToAccount() : null;

            return Task.FromResult(account);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
