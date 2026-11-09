namespace VoucherCampaigns.Application.Authentication;

/// <summary>Troca a credencial da aplicação satélite por um token de acesso (US-07.5, client credentials).</summary>
public interface ISatelliteTokenService
{
    /// <returns>null quando a credencial é inválida ou a aplicação está inativa.</returns>
    Task<SatelliteTokenResponse?> IssueAsync(SatelliteTokenRequest request, CancellationToken cancellationToken);
}
