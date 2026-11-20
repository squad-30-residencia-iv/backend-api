namespace VoucherCampaigns.Application.Authentication;

/// <summary>
/// Bloqueio do <c>POST /satellite/token</c> depois de segredos errados, por <c>clientId</c> + origem (spec GH-071, D-2):
/// a mesma credencial errada vinda de outro endereço não bloqueia o PDV legítimo.
/// </summary>
public interface ISatelliteTokenRateLimiter
{
    bool IsLockedOut(string clientId, string? origin = null);
    void RecordFailure(string clientId, string? origin = null);
    void Reset(string clientId, string? origin = null);
}
