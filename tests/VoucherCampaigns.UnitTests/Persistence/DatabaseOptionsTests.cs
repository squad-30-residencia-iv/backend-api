using VoucherCampaigns.Infrastructure.Persistence;

namespace VoucherCampaigns.UnitTests.Persistence;

public class DatabaseOptionsTests
{
    [Theory]
    [InlineData("Sqlite", DatabaseProvider.Sqlite)]
    [InlineData("sqlite", DatabaseProvider.Sqlite)]
    [InlineData("Postgres", DatabaseProvider.Postgres)]
    [InlineData(" POSTGRES ", DatabaseProvider.Postgres)]
    public void ResolveProvider_AcceptsKnownProvidersIgnoringCase(string provider, DatabaseProvider expected)
    {
        var options = new DatabaseOptions { Provider = provider, ConnectionString = "any" };

        Assert.Equal(expected, options.ResolveProvider());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Oracle")]
    [InlineData("0")]
    public void Validate_ThrowsWhenProviderIsUnknown(string provider)
    {
        var options = new DatabaseOptions { Provider = provider, ConnectionString = "any" };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Database:Provider", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ThrowsWhenConnectionStringIsEmpty(string connectionString)
    {
        var options = new DatabaseOptions { Provider = "Sqlite", ConnectionString = connectionString };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Database:ConnectionString", exception.Message);
    }

    [Fact]
    public void Validate_DoesNotThrowWhenOptionsAreValid()
    {
        var options = new DatabaseOptions { Provider = "Postgres", ConnectionString = "Host=localhost" };

        options.Validate();
    }
}
