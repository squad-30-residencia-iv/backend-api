using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VoucherCampaigns.IntegrationTests.Authentication;

namespace VoucherCampaigns.IntegrationTests;

public class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Connection string de um PostgreSQL (sem o nome do banco) para rodar a suíte nele, ex.:
    /// TEST_POSTGRES_CONNECTION="Host=localhost;Port=5432;Username=postgres;Password=postgres".
    /// Sem a variável, os testes usam SQLite.
    /// </summary>
    public const string PostgresVariable = "TEST_POSTGRES_CONNECTION";

    private static readonly string? PostgresServer = Environment.GetEnvironmentVariable(PostgresVariable);

    private readonly bool _ownsDatabaseFile;
    private readonly string? _seedDemoData;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    /// <summary>Banco novo e exclusivo desta fábrica: arquivo SQLite temporário ou banco PostgreSQL descartável.</summary>
    public ApiFactory()
        : this(
            DefaultProvider,
            PostgresServer is null ? NewSqliteConnectionString() : NewPostgresConnectionString(PostgresServer),
            ownsDatabaseFile: true,
            seedDemoData: null,
            settings: null)
    {
    }

    public static string DefaultProvider => PostgresServer is null ? "Sqlite" : "Postgres";

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

    private static string NewPostgresConnectionString(string server) =>
        new NpgsqlConnectionStringBuilder(server) { Database = $"vc_tests_{Guid.NewGuid():N}" }.ConnectionString;

    private static void DropPostgresDatabase(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var database = builder.Database;
        NpgsqlConnection.ClearAllPools();

        builder.Database = "postgres";
        using var connection = new NpgsqlConnection(builder.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)";
        command.ExecuteNonQuery();
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
            if (Provider == "Postgres")
            {
                DropPostgresDatabase(ConnectionString);
            }
            else
            {
                DeleteSqliteDatabase(ConnectionString);
            }
        }
    }
}
