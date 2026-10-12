using System.Net.Http.Json;
using VoucherCampaigns.Application.SystemStatus;

namespace VoucherCampaigns.IntegrationTests;

public class StartupTests
{
    [Fact]
    public async Task Startup_OverAlreadyMigratedDatabase_DoesNotReapplyMigrations()
    {
        var connectionString = ApiFactory.NewSqliteConnectionString();

        try
        {
            var first = await GetStatusAsync(connectionString);
            var second = await GetStatusAsync(connectionString);

            Assert.Single(first.AppliedMigrations);
            Assert.Equal(first.AppliedMigrations, second.AppliedMigrations);
        }
        finally
        {
            ApiFactory.DeleteSqliteDatabase(connectionString);
        }
    }

    [Theory]
    [InlineData("Oracle", "Data Source=unused.db")]
    [InlineData("Sqlite", "")]
    public void Startup_WithInvalidDatabaseConfiguration_Fails(string provider, string connectionString)
    {
        using var factory = ApiFactory.For(provider, connectionString);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Configuração inválida", exception.Message);
    }

    private static async Task<SystemStatusResponse> GetStatusAsync(string connectionString)
    {
        using var factory = ApiFactory.For("Sqlite", connectionString);

        var status = await factory.CreateClient().GetFromJsonAsync<SystemStatusResponse>("/api/v1/system/status");

        return status!;
    }
}
