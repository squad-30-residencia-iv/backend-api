using System.Security.Cryptography;

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

    /// <summary>
    /// Carimbo que vai no token (claim <c>stamp</c>, spec GH-071 D-1). Muda ao inativar, trocar o segredo ou mudar os
    /// escopos: o token emitido antes deixa de valer na hora, sem esperar a expiração.
    /// </summary>
    public string SecurityStamp { get; set; } = NewStamp();

    public void RotateStamp() => SecurityStamp = NewStamp();

    public static string NewStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public DateTime CreatedAt { get; set; }
}
