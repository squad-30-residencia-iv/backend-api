namespace VoucherCampaigns.Application.Vouchers;

/// <summary>Libera as reservas provisórias vencidas (PR-17, RN09b): voucher descartado, saldo devolvido, concessão expirada.</summary>
public interface IReservationReleaser
{
    /// <returns>Quantidade de reservas liberadas.</returns>
    Task<int> ReleaseExpiredAsync(Guid? networkId, DateTime utcNow, CancellationToken cancellationToken);
}
