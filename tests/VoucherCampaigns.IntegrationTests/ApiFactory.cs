using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.IntegrationTests.Authentication;

namespace VoucherCampaigns.IntegrationTests;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly bool _ownsDatabaseFile;
    private readonly string? _seedDemoData;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    public ApiFactory()
        : this("Sqlite", NewSqliteConnectionString(), ownsDatabaseFile: true, seedDemoData: null, settings: null)
    {
    }

    private ApiFactory(
        string provider,
        string connectionString,
        bool ownsDatabaseFile,
        string? seedDemoData,
        IReadOnlyDictionary<string, string?>? settings)
    {
        Provider = provider;
        ConnectionString = connectionString;
        _ownsDatabaseFile = ownsDatabaseFile;
        _seedDemoData = seedDemoData;
        _settings = settings ?? new Dictionary<string, string?>();
    }

    public string Provider { get; }

    public string ConnectionString { get; }

    /// <summary>Relógio da Api (TimeProvider); fixe <see cref="TestClock.UtcNow"/> para simular "hoje".</summary>
    public TestClock Clock { get; } = new();

    /// <param name="seedDemoData">Sobrescreve Database:SeedDemoData; null mantém o appsettings do ambiente (Development = true).</param>
    /// <param name="settings">Chaves extras de configuração (ex.: Auth:Jwt:SigningKey).</param>
    public static ApiFactory For(
        string provider,
        string connectionString,
        string? seedDemoData = null,
        IReadOnlyDictionary<string, string?>? settings = null) =>
        new(provider, connectionString, ownsDatabaseFile: false, seedDemoData, settings);

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

        foreach (var (key, value) in _settings)
        {
            settings[key] = value;
        }

        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));

        // Rotas usadas só pelos testes de autenticação e escopo; não existem na Api.
        builder.ConfigureTestServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(AuthTestController).Assembly);
            services.AddSingleton<TimeProvider>(Clock);
        });
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
