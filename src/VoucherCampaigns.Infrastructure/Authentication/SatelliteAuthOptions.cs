using Microsoft.Extensions.Configuration;
using VoucherCampaigns.Application.Authentication;

namespace VoucherCampaigns.Infrastructure.Authentication;

/// <summary>Seção <c>Auth:Satellite</c>: token e carga inicial das aplicações satélites (US-07.5).</summary>
public record SatelliteAuthOptions
{
    public const string SectionName = AuthOptions.SectionName + ":Satellite";
    public const string DefaultAudience = "vouchercampaigns-satellite";

    private const string AppsSection = SectionName + ":Apps";

    /// <summary>Audiência própria: o token da aplicação não vale nas rotas de usuário, e vice-versa.</summary>
    public string Audience { get; init; } = DefaultAudience;

    public int ExpirationMinutes { get; init; } = AuthOptions.DefaultExpirationMinutes;

    /// <summary>Carga inicial de <c>satellite_apps</c> quando a tabela está vazia. A emissão do token não lê esta lista.</summary>
    public IReadOnlyList<SatelliteAppOptions> Apps { get; init; } = [];

    public static SatelliteAuthOptions FromConfiguration(IConfiguration configuration, AuthOptions userAuth)
    {
        var section = configuration.GetSection(SectionName);
        var audience = section[nameof(Audience)];

        var options = new SatelliteAuthOptions
        {
            Audience = string.IsNullOrWhiteSpace(audience) ? DefaultAudience : audience.Trim(),
            ExpirationMinutes = AuthOptions.ParseExpirationMinutes(
                section[nameof(ExpirationMinutes)], $"{SectionName}:{nameof(ExpirationMinutes)}"),
            Apps = configuration.GetSection(AppsSection).GetChildren()
                .Select(app => new SatelliteAppOptions
                {
                    ClientId = app[nameof(SatelliteAppOptions.ClientId)] ?? string.Empty,
                    ClientSecret = app[nameof(SatelliteAppOptions.ClientSecret)] ?? string.Empty,
                    Name = app[nameof(SatelliteAppOptions.Name)] ?? string.Empty,
                    NetworkId = app[nameof(SatelliteAppOptions.NetworkId)] ?? string.Empty,
                    Scopes = ReadScopes(app.GetSection(nameof(SatelliteAppOptions.Scopes)))
                })
                .ToList()
        };

        options.Validate(userAuth);

        return options;
    }

    public void Validate(AuthOptions userAuth)
    {
        if (string.Equals(Audience, userAuth.Audience, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{SectionName}:{nameof(Audience)}' deve ser diferente de '{AuthOptions.SectionName}:Jwt:Audience'.");
        }

        if (ExpirationMinutes <= 0)
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{SectionName}:{nameof(ExpirationMinutes)}' deve ser maior que zero.");
        }

        for (var index = 0; index < Apps.Count; index++)
        {
            Apps[index].Validate($"{AppsSection}:{index}");
        }

        var duplicatedClient = Apps
            .GroupBy(app => app.ClientId.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicatedClient is not null)
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{AppsSection}' repete o ClientId '{duplicatedClient.Key}'.");
        }
    }

    // Lista ("Scopes": [..] ou Scopes__0, Scopes__1) ou texto separado por espaço, mais prático em variável de ambiente.
    private static List<string> ReadScopes(IConfigurationSection section)
    {
        var values = section.Value is { } inline
            ? inline.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : section.GetChildren().Select(scope => scope.Value ?? string.Empty);

        return values.Select(scope => scope.Trim()).ToList();
    }
}

public record SatelliteAppOptions
{
    public const int MinimumSecretLength = 32;
    public const int MaxClientIdLength = 100;
    public const int MaxNameLength = 150;

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string NetworkId { get; init; } = string.Empty;

    public IReadOnlyList<string> Scopes { get; init; } = [];

    public void Validate(string path)
    {
        var clientId = ClientId.Trim();

        if (clientId.Length == 0 || clientId.Length > MaxClientIdLength || clientId.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{path}:{nameof(ClientId)}' deve ter de 1 a {MaxClientIdLength} caracteres, sem espaços.");
        }

        if (ClientSecret.Length < MinimumSecretLength)
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{path}:{nameof(ClientSecret)}' deve ter ao menos {MinimumSecretLength} caracteres.");
        }

        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > MaxNameLength)
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{path}:{nameof(Name)}' deve ter de 1 a {MaxNameLength} caracteres.");
        }

        if (!Guid.TryParse(NetworkId, out var networkId) || networkId == Guid.Empty)
        {
            throw new InvalidOperationException($"Configuração inválida: '{path}:{nameof(NetworkId)}' = '{NetworkId}' não é um Guid.");
        }

        if (Scopes.Count == 0)
        {
            throw new InvalidOperationException($"Configuração inválida: '{path}:{nameof(Scopes)}' precisa de ao menos um escopo.");
        }

        var unknown = Scopes.FirstOrDefault(scope => !SatelliteScopes.All.Contains(scope));

        if (unknown is not null)
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{path}:{nameof(Scopes)}' tem o escopo '{unknown}'. " +
                $"Valores aceitos: {string.Join(", ", SatelliteScopes.All)}.");
        }
    }
}
