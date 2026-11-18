using VoucherCampaigns.Application.Authentication;

namespace VoucherCampaigns.UnitTests.Authentication;

public class SatelliteScopesTests
{
    [Theory]
    [InlineData("vouchers:issue", SatelliteScopes.IssueVouchers)]
    [InlineData("voucher:issue", SatelliteScopes.IssueVouchers)]
    [InlineData(" Campaign:Read ", SatelliteScopes.ReadCampaigns)]
    [InlineData("voucher:redeem", SatelliteScopes.RedeemVouchers)]
    public void TryNormalize_AcceptsTheCanonicalScopesAndTheSingularAliases(string scope, string expected)
    {
        Assert.True(SatelliteScopes.TryNormalize(scope, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void TryNormalize_RefusesVoucherRead_BecauseItWouldGrantIssuance()
    {
        // SEC-04 da auditoria (spec GH-066, D-5).
        Assert.False(SatelliteScopes.TryNormalize("voucher:read", out _));
    }
}
