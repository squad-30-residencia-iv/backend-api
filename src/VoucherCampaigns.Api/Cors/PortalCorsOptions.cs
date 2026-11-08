using Microsoft.Extensions.Configuration;

namespace VoucherCampaigns.Api.Cors;

/// <summary>Origens do portal autorizadas a chamar a API a partir de outro endereço.</summary>
public sealed record PortalCorsOptions
{
    public const string SectionName = "Cors";
    public const string PolicyName = "Portal";

    private const string OriginsSection = SectionName + ":AllowedOrigins";

    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

    public static PortalCorsOptions FromConfiguration(IConfiguration configuration)
    {
        var options = new PortalCorsOptions
        {
            AllowedOrigins = configuration.GetSection(OriginsSection).GetChildren()
                .Select(origin => origin.Value ?? string.Empty)
                .ToList()
        };

        options.Validate();

        return options;
    }

    public void Validate()
    {
        for (var index = 0; index < AllowedOrigins.Count; index++)
        {
            if (!IsOrigin(AllowedOrigins[index]))
            {
                throw new InvalidOperationException(
                    $"Configuração inválida: '{OriginsSection}:{index}' = '{AllowedOrigins[index]}' não é uma origem absoluta.");
            }
        }
    }

    /// <summary>Esquema http ou https, com host e sem caminho, consulta ou fragmento.</summary>
    public static bool IsOrigin(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.EndsWith('/'))
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && (string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/")
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }
}
