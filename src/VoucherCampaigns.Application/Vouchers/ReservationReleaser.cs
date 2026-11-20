using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Application.Vouchers;

/// <summary>
/// Liberação das reservas vencidas (spec GH-045, D-6). Cada voucher é descartado por um comando condicional
/// pelo status atual, numa transação própria: dois chamadores simultâneos não devolvem o saldo duas vezes.
/// Um provisório resgatado na própria compra (GH-049, D-10) tem o resgate estornado junto.
/// </summary>
public class ReservationReleaser(IConcessionRepository concessions, IRedemptionReverser redemptionReverser) : IReservationReleaser
{
    public async Task<int> ReleaseExpiredAsync(Guid? networkId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var released = 0;

        foreach (var expired in await concessions.GetExpiredReservationsAsync(networkId, utcNow, cancellationToken))
        {
            await using var transaction = await concessions.BeginTransactionAsync(cancellationToken);

            // Campanha antes do voucher, na ordem do cancelamento (GH-061, 7.1); depois, quem conseguir mudar o status
            // é quem devolve o saldo.
            await concessions.LockCampaignAsync(expired.CampaignId, cancellationToken);

            if (!await concessions.TryDiscardVoucherAsync(expired.VoucherId, utcNow, cancellationToken))
            {
                continue;
            }

            await concessions.ReleaseReservationAsync(expired.CampaignId, expired.CampaignItemId, cancellationToken);
            await concessions.MarkConcessionAsync(expired.ConcessionId, ConcessionStatus.Expired, utcNow, cancellationToken);
            await redemptionReverser.ReverseAllOfVoucherAsync(expired.VoucherId, "ReservationExpired", null, utcNow, cancellationToken);
            concessions.Add(new VoucherEvent
            {
                Id = Guid.CreateVersion7(new DateTimeOffset(utcNow)),
                VoucherId = expired.VoucherId,
                EventType = VoucherEventTypes.ReservationExpired,
                OccurredAt = utcNow
            });
            await concessions.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            released++;
        }

        return released;
    }
}
