namespace VoucherCampaigns.Application.Authentication;

/// <summary>Fonte das credenciais das aplicações satélites. A implementação atual lê a tabela <c>satellite_apps</c>.</summary>
public interface ISatelliteCredentialStore
{
    /// <returns>null quando o cliente não existe, o segredo não confere ou a aplicação está inativa.</returns>
    Task<SatelliteAppAccount?> FindByCredentialsAsync(string clientId, string clientSecret, CancellationToken cancellationToken);
}

public record SatelliteAppAccount(Guid ApplicationId, string ClientId, Guid NetworkId, IReadOnlyList<string> Scopes);
