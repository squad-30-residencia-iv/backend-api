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

            Assert.Contains(first.AppliedMigrations, migration => migration.EndsWith("_CoreTables"));
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

    [Fact]
    public void Startup_WithInvalidSeedDemoData_Fails()
    {
        var connectionString = ApiFactory.NewSqliteConnectionString();

        try
        {
            using var factory = ApiFactory.For("Sqlite", connectionString, seedDemoData: "sim");

            var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

            Assert.Contains("Database:SeedDemoData", exception.Message);
        }
        finally
        {
            ApiFactory.DeleteSqliteDatabase(connectionString);
        }
    }

    [Theory]
    [InlineData("Auth:Jwt:SigningKey", "", "Auth:Jwt:SigningKey")]
    [InlineData("Auth:Jwt:SigningKey", "curta-demais", "Auth:Jwt:SigningKey")]
    [InlineData("Auth:Jwt:ExpirationMinutes", "uma-hora", "Auth:Jwt:ExpirationMinutes")]
    [InlineData("Auth:MockUsers:0:Role", "admin", "Auth:MockUsers:0:Role")]
    public void Startup_WithInvalidAuthConfiguration_Fails(string key, string value, string expectedKey)
    {
        var connectionString = ApiFactory.NewSqliteConnectionString();

        try
        {
            using var factory = ApiFactory.For("Sqlite", connectionString, settings: new Dictionary<string, string?> { [key] = value });

            var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

            Assert.Contains("Configuração inválida", exception.Message);
            Assert.Contains(expectedKey, exception.Message);
        }
        finally
        {
            ApiFactory.DeleteSqliteDatabase(connectionString);
        }
    }

    private static async Task<SystemStatusResponse> GetStatusAsync(string connectionString)
    {
        using var factory = ApiFactory.For("Sqlite", connectionString);

        var status = await factory.CreateClient().GetFromJsonAsync<SystemStatusResponse>("/api/v1/system/status");

        return status!;
    }
}
