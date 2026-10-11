using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace VoucherCampaigns.IntegrationTests;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly bool _ownsDatabaseFile;

    public ApiFactory()
        : this("Sqlite", NewSqliteConnectionString(), ownsDatabaseFile: true)
    {
    }

    private ApiFactory(string provider, string connectionString, bool ownsDatabaseFile)
    {
        Provider = provider;
        ConnectionString = connectionString;
        _ownsDatabaseFile = ownsDatabaseFile;
    }

    public string Provider { get; }

    public string ConnectionString { get; }

    public static ApiFactory For(string provider, string connectionString) =>
        new(provider, connectionString, ownsDatabaseFile: false);

    public static string NewSqliteConnectionString() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), $"vouchercampaigns-tests-{Guid.NewGuid():N}.db")}";

    public static void DeleteSqliteDatabase(string connectionString)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(new SqliteConnectionStringBuilder(connectionString).DataSource);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = Provider,
                ["Database:ConnectionString"] = ConnectionString
            }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && _ownsDatabaseFile)
        {
            DeleteSqliteDatabase(ConnectionString);
        }
    }
}
