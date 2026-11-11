namespace VoucherCampaigns.Domain.Identity;

/// <summary>Aplicação satélite autorizada a chamar a API (US-07.5). O segredo fica só como hash.</summary>
public class SatelliteApp
{
    public Guid Id { get; set; }

    /// <summary>Identificador público da aplicação, em minúsculas.</summary>
    public string ClientId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string SecretHash { get; set; } = string.Empty;

    /// <summary>Rede em que a aplicação atua.</summary>
    public Guid NetworkId { get; set; }

    /// <summary>Escopos concedidos, separados por espaço.</summary>
    public string Scopes { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}
