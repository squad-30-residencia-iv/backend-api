namespace VoucherCampaigns.Application.Authentication;

/// <summary>Aplicação satélite autenticada na requisição atual (US-07.5). Não é um usuário: use em rotas de satélite.</summary>
public interface ICurrentSatelliteApp
{
    Guid ApplicationId { get; }

    string ClientId { get; }

    /// <summary>Rede em que a aplicação atua.</summary>
    Guid NetworkId { get; }

    IReadOnlyList<string> Scopes { get; }
}
