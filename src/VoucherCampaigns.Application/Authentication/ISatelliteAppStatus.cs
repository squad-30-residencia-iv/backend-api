namespace VoucherCampaigns.Application.Authentication;

/// <summary>
/// Estado atual da aplicação satélite, conferido a cada requisição com token (spec GH-071, D-1; SEC-03 da auditoria):
/// aplicação inativa, segredo trocado ou escopos mudados invalidam o token emitido antes, sem esperar a expiração.
/// </summary>
public interface ISatelliteAppStatus
{
    /// <summary>A aplicação existe, está ativa e ainda tem o carimbo que veio no token.</summary>
    Task<bool> IsCurrentAsync(Guid applicationId, string? stamp, CancellationToken cancellationToken);

    /// <summary>Descarta o estado guardado desta instância depois de uma mudança na aplicação.</summary>
    void Forget(Guid applicationId);
}
