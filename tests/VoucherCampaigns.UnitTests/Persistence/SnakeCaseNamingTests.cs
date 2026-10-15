using VoucherCampaigns.Infrastructure.Persistence;

namespace VoucherCampaigns.UnitTests.Persistence;

public class SnakeCaseNamingTests
{
    [Theory]
    [InlineData("Id", "id")]
    [InlineData("NetworkId", "network_id")]
    [InlineData("TotalVoucherLimit", "total_voucher_limit")]
    [InlineData("PK_networks", "pk_networks")]
    [InlineData("IX_regions_network_id_name", "ix_regions_network_id_name")]
    [InlineData("already_snake", "already_snake")]
    public void ToSnakeCase_ConvertsNames(string name, string expected)
    {
        Assert.Equal(expected, SnakeCaseNaming.ToSnakeCase(name));
    }
}
