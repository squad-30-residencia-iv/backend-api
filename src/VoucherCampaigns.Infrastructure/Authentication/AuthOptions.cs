using System.Globalization;
using Microsoft.Extensions.Configuration;
using VoucherCampaigns.Application.Authentication;

namespace VoucherCampaigns.Infrastructure.Authentication;

public record AuthOptions
{
    public const string SectionName = "Auth";
    public const int MinimumSigningKeyLength = 32;
    public const int DefaultExpirationMinutes = 60;

    private const string JwtSection = SectionName + ":Jwt";
    private const string MockUsersSection = SectionName + ":MockUsers";

    public string SigningKey { get; init; } = string.Empty;

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public int ExpirationMinutes { get; init; } = DefaultExpirationMinutes;

    /// <summary>Usuários provisórios enquanto a origem das contas não é definida (somente para ambiente local).</summary>
    public IReadOnlyList<MockUserOptions> MockUsers { get; init; } = [];

    public static AuthOptions FromConfiguration(IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtSection);

        var options = new AuthOptions
        {
            SigningKey = jwt[nameof(SigningKey)] ?? string.Empty,
            Issuer = jwt[nameof(Issuer)] ?? string.Empty,
            Audience = jwt[nameof(Audience)] ?? string.Empty,
            ExpirationMinutes = ParseExpirationMinutes(jwt[nameof(ExpirationMinutes)]),
            MockUsers = configuration.GetSection(MockUsersSection).GetChildren()
                .Select(user => new MockUserOptions
                {
                    Email = user[nameof(MockUserOptions.Email)] ?? string.Empty,
                    Password = user[nameof(MockUserOptions.Password)] ?? string.Empty,
                    UserId = user[nameof(MockUserOptions.UserId)] ?? string.Empty,
                    Role = user[nameof(MockUserOptions.Role)] ?? string.Empty,
                    NetworkId = user[nameof(MockUserOptions.NetworkId)] ?? string.Empty
                })
                .ToList()
        };

        options.Validate();

        return options;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SigningKey) || SigningKey.Length < MinimumSigningKeyLength)
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{JwtSection}:{nameof(SigningKey)}' deve ter ao menos {MinimumSigningKeyLength} caracteres.");
        }

        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException($"Configuração inválida: '{JwtSection}:{nameof(Issuer)}' não pode ser vazio.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException($"Configuração inválida: '{JwtSection}:{nameof(Audience)}' não pode ser vazio.");
        }

        if (ExpirationMinutes <= 0)
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{JwtSection}:{nameof(ExpirationMinutes)}' deve ser maior que zero.");
        }

        for (var index = 0; index < MockUsers.Count; index++)
        {
            MockUsers[index].Validate($"{MockUsersSection}:{index}");
        }

        var duplicatedEmail = MockUsers
            .GroupBy(user => user.Email.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicatedEmail is not null)
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{MockUsersSection}' repete o e-mail '{duplicatedEmail.Key}'.");
        }
    }

    private static int ParseExpirationMinutes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultExpirationMinutes;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            ? minutes
            : throw new InvalidOperationException(
                $"Configuração inválida: '{JwtSection}:{nameof(ExpirationMinutes)}' = '{value}'. Informe um número inteiro de minutos.");
    }
}

public record MockUserOptions
{
    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string UserId { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public string NetworkId { get; init; } = string.Empty;

    /// <summary>Converte após <see cref="Validate"/>; os IDs já foram conferidos.</summary>
    public UserAccount ToAccount() => new(Guid.Parse(UserId), Email.Trim(), Role, Guid.Parse(NetworkId));

    public void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            throw new InvalidOperationException($"Configuração inválida: '{path}' precisa de {nameof(Email)} e {nameof(Password)}.");
        }

        if (!Guid.TryParse(UserId, out _))
        {
            throw new InvalidOperationException($"Configuração inválida: '{path}:{nameof(UserId)}' = '{UserId}' não é um Guid.");
        }

        if (!Guid.TryParse(NetworkId, out _))
        {
            throw new InvalidOperationException($"Configuração inválida: '{path}:{nameof(NetworkId)}' = '{NetworkId}' não é um Guid.");
        }

        if (!Roles.All.Contains(Role))
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{path}:{nameof(Role)}' = '{Role}'. Valores aceitos: {string.Join(", ", Roles.All)}.");
        }
    }
}
