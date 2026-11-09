using VoucherCampaigns.Infrastructure.Authentication;

namespace VoucherCampaigns.UnitTests.Authentication;

public class SatelliteAuthOptionsTests
{
    private static readonly AuthOptions UserAuth = AuthOptionsTests.ValidOptions();

    public static SatelliteAuthOptions ValidOptions() => new()
    {
        Audience = SatelliteAuthOptions.DefaultAudience,
        ExpirationMinutes = 60,
        Apps = [ValidApp("pdv-demo")]
    };

    public static SatelliteAppOptions ValidApp(string clientId) => new()
    {
        ClientId = clientId,
        ClientSecret = new string('s', SatelliteAppOptions.MinimumSecretLength),
        Name = "PDV de demonstração",
        NetworkId = "0199e3a0-0000-7000-8000-000000000001",
        Scopes = ["vouchers:issue"]
    };

    [Fact]
    public void Validate_DoesNotThrowWhenOptionsAreValid()
    {
        ValidOptions().Validate(UserAuth);
    }

    [Fact]
    public void Validate_ThrowsWhenAudienceEqualsTheUserAudience()
    {
        var options = ValidOptions() with { Audience = UserAuth.Audience.ToUpperInvariant() };

        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate(UserAuth));

        Assert.Contains("Auth:Satellite:Audience", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pdv demo")]
    public void Validate_ThrowsWhenClientIdIsEmptyOrHasSpaces(string clientId)
    {
        var options = ValidOptions() with { Apps = [ValidApp(clientId)] };

        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate(UserAuth));

        Assert.Contains("Auth:Satellite:Apps:0:ClientId", exception.Message);
    }

    [Fact]
    public void Validate_ThrowsWhenClientIdRepeatsIgnoringCase()
    {
        var options = ValidOptions() with { Apps = [ValidApp("pdv-demo"), ValidApp("PDV-Demo")] };

        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate(UserAuth));

        Assert.Contains("repete o ClientId", exception.Message);
    }

    [Fact]
    public void Validate_ThrowsWhenSecretIsShort()
    {
        var app = ValidApp("pdv-demo") with { ClientSecret = new string('s', SatelliteAppOptions.MinimumSecretLength - 1) };

        var exception = Assert.Throws<InvalidOperationException>(() => (ValidOptions() with { Apps = [app] }).Validate(UserAuth));

        Assert.Contains("Auth:Satellite:Apps:0:ClientSecret", exception.Message);
    }

    [Theory]
    [InlineData("rede-norte")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Validate_ThrowsWhenNetworkIsNotAGuid(string networkId)
    {
        var app = ValidApp("pdv-demo") with { NetworkId = networkId };

        var exception = Assert.Throws<InvalidOperationException>(() => (ValidOptions() with { Apps = [app] }).Validate(UserAuth));

        Assert.Contains("Auth:Satellite:Apps:0:NetworkId", exception.Message);
    }

    [Fact]
    public void Validate_ThrowsWhenScopesAreEmptyOrUnknown()
    {
        var empty = ValidApp("pdv-demo") with { Scopes = [] };
        var unknown = ValidApp("pdv-demo") with { Scopes = ["vouchers:issue", "Vouchers:Redeem"] };

        var emptyError = Assert.Throws<InvalidOperationException>(() => (ValidOptions() with { Apps = [empty] }).Validate(UserAuth));
        var unknownError = Assert.Throws<InvalidOperationException>(() => (ValidOptions() with { Apps = [unknown] }).Validate(UserAuth));

        Assert.Contains("ao menos um escopo", emptyError.Message);
        Assert.Contains("'Vouchers:Redeem'", unknownError.Message);
    }
}
