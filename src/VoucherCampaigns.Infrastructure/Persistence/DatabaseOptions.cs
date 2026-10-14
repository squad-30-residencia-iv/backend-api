using Microsoft.Extensions.Configuration;

namespace VoucherCampaigns.Infrastructure.Persistence;

public record DatabaseOptions
{
    public const string SectionName = "Database";

    public string Provider { get; init; } = string.Empty;

    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>Insere os dados de demonstração após as migrações (somente para ambiente local).</summary>
    public bool SeedDemoData { get; init; }

    public static DatabaseOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        var options = new DatabaseOptions
        {
            Provider = section[nameof(Provider)] ?? string.Empty,
            ConnectionString = section[nameof(ConnectionString)] ?? string.Empty,
            SeedDemoData = ParseSeedDemoData(section[nameof(SeedDemoData)])
        };

        options.Validate();

        return options;
    }

    public DatabaseProvider ResolveProvider() => Provider.Trim().ToLowerInvariant() switch
    {
        "sqlite" => DatabaseProvider.Sqlite,
        "postgres" => DatabaseProvider.Postgres,
        _ => throw new InvalidOperationException(
            $"Configuração inválida: '{SectionName}:{nameof(Provider)}' = '{Provider}'. " +
            $"Valores aceitos: {string.Join(", ", Enum.GetNames<DatabaseProvider>())}.")
    };

    public void Validate()
    {
        ResolveProvider();

        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{SectionName}:{nameof(ConnectionString)}' não pode ser vazia.");
        }
    }

    private static bool ParseSeedDemoData(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return bool.TryParse(value, out var seed)
            ? seed
            : throw new InvalidOperationException(
                $"Configuração inválida: '{SectionName}:{nameof(SeedDemoData)}' = '{value}'. Valores aceitos: true, false.");
    }
}
