using VoucherCampaigns.Infrastructure.Authentication;

namespace VoucherCampaigns.UnitTests.Authentication;

public class AuthOptionsTests
{
    public static AuthOptions ValidOptions() => new()
    {
        SigningKey = new string('k', AuthOptions.MinimumSigningKeyLength),
        Issuer = "issuer",
        Audience = "audience",
        ExpirationMinutes = 60,
        MockUsers = [ValidUser("analista@empresa.com")]
    };

    public static MockUserOptions ValidUser(string email, string role = "analista") => new()
    {
        Email = email,
        Password = "Senha123",
        UserId = "0199e3a0-0000-7000-8000-000000000901",
        Role = role,
        NetworkId = "0199e3a0-0000-7000-8000-000000000001"
    };

    [Fact]
    public void Validate_DoesNotThrowWhenOptionsAreValid()
    {
        ValidOptions().Validate();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("chave-com-31-caracteres-0000000")]
    public void Validate_ThrowsWhenSigningKeyIsMissingOrShort(string signingKey)
    {
        var options = ValidOptions() with { SigningKey = signingKey };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Auth:Jwt:SigningKey", exception.Message);
    }

    [Fact]
    public void Validate_ThrowsWhenIssuerIsEmpty()
    {
        var exception = Assert.Throws<InvalidOperationException>((ValidOptions() with { Issuer = "" }).Validate);

        Assert.Contains("Auth:Jwt:Issuer", exception.Message);
    }

    [Fact]
    public void Validate_ThrowsWhenAudienceIsEmpty()
    {
        var exception = Assert.Throws<InvalidOperationException>((ValidOptions() with { Audience = " " }).Validate);

        Assert.Contains("Auth:Jwt:Audience", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_ThrowsWhenExpirationIsNotPositive(int minutes)
    {
        var options = ValidOptions() with { ExpirationMinutes = minutes };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Auth:Jwt:ExpirationMinutes", exception.Message);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("Analista")]
    [InlineData("")]
    public void Validate_ThrowsWhenMockUserRoleIsNotOfficial(string role)
    {
        var options = ValidOptions() with { MockUsers = [ValidUser("analista@empresa.com", role)] };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Auth:MockUsers:0:Role", exception.Message);
    }

    [Theory]
    [InlineData(nameof(MockUserOptions.UserId))]
    [InlineData(nameof(MockUserOptions.NetworkId))]
    public void Validate_ThrowsWhenMockUserIdIsNotGuid(string property)
    {
        var user = ValidUser("analista@empresa.com");
        user = property == nameof(MockUserOptions.UserId) ? user with { UserId = "tenant_abc" } : user with { NetworkId = "tenant_abc" };

        var exception = Assert.Throws<InvalidOperationException>((ValidOptions() with { MockUsers = [user] }).Validate);

        Assert.Contains($"Auth:MockUsers:0:{property}", exception.Message);
    }

    [Fact]
    public void Validate_ThrowsWhenMockUserEmailIsRepeated()
    {
        var options = ValidOptions() with
        {
            MockUsers = [ValidUser("analista@empresa.com"), ValidUser("ANALISTA@empresa.com", "gestor")]
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Auth:MockUsers", exception.Message);
    }
}
