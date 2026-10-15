using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace VoucherCampaigns.IntegrationTests;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly bool _ownsDatabaseFile;
    private readonly string? _seedDemoData;

    public ApiFactory()
        : this("Sqlite", NewSqliteConnectionString(), ownsDatabaseFile: true, seedDemoData: null)
    {
    }

    private ApiFactory(string provider, string connectionString, bool ownsDatabaseFile, string? seedDemoData)
    {
        Provider = provider;
        ConnectionString = connectionString;
        _ownsDatabaseFile = ownsDatabaseFile;
        _seedDemoData = seedDemoData;
    }

    public string Provider { get; }

    public string ConnectionString { get; }

    /// <param name="seedDemoData">Sobrescreve Database:SeedDemoData; null mantém o appsettings do ambiente (Development = true).</param>
    public static ApiFactory For(string provider, string connectionString, string? seedDemoData = null) =>
        new(provider, connectionString, ownsDatabaseFile: false, seedDemoData);

    public static string NewSqliteConnectionString() =>
        $"Data Source={Path.Combine(Path.GetTempPath(), $"vouchercampaigns-tests-{Guid.NewGuid():N}.db")}";

    public static void DeleteSqliteDatabase(string connectionString)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(new SqliteConnectionStringBuilder(connectionString).DataSource);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = Provider,
            ["Database:ConnectionString"] = ConnectionString
        };

        if (_seedDemoData is not null)
        {
            settings["Database:SeedDemoData"] = _seedDemoData;
        }

        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
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
