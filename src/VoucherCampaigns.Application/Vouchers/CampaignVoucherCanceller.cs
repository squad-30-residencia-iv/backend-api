using VoucherCampaigns.Application.Audit;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Application.Vouchers;

/// <summary>Contagens do cancelamento, para a auditoria da campanha (spec GH-061, D-10).</summary>
public record CampaignVoucherCancellation(int CancelledVouchers, int DiscardedProvisional, int ReversedRedemptions);

/// <summary>Invalida os vouchers não utilizados da campanha cancelada (RN10, US-05.5; spec GH-061).</summary>
public interface ICampaignVoucherCanceller
{
    /// <summary>
    /// Roda dentro da transação do cancelamento, depois da trava da campanha: descarta os provisórios em dia (D-6),
    /// cancela os disponíveis dentro da validade (D-5) e registra um evento por voucher (D-8). Não grava: quem chamou salva.
    /// </summary>
    Task<CampaignVoucherCancellation> CancelVouchersAsync(Guid campaignId, Guid actorUserId, DateTime utcNow, CancellationToken cancellationToken);
}

public class CampaignVoucherCanceller(IConcessionRepository concessions, IRedemptionReverser redemptionReverser) : ICampaignVoucherCanceller
{
    /// <summary>Causa gravada no evento do voucher e no estorno (D-8).</summary>
    public const string Cause = "CampaignCancelled";

    public async Task<CampaignVoucherCancellation> CancelVouchersAsync(
        Guid campaignId, Guid actorUserId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var discarded = 0;
        var reversed = 0;

        // Mesmo caminho do desfazimento da concessão: quem descarta é quem devolve a reserva (D-6).
        foreach (var provisional in await concessions.GetLiveProvisionalAsync(campaignId, utcNow, cancellationToken))
        {
            if (!await concessions.TryDiscardVoucherAsync(provisional.VoucherId, utcNow, cancellationToken, cancelledByCampaign: true))
            {
                continue;
            }

            await concessions.ReleaseReservationAsync(provisional.CampaignId, provisional.CampaignItemId, cancellationToken);
            await concessions.MarkConcessionAsync(provisional.ConcessionId, ConcessionStatus.Cancelled, utcNow, cancellationToken);
            reversed += await redemptionReverser.ReverseAllOfVoucherAsync(provisional.VoucherId, Cause, null, utcNow, cancellationToken);
            concessions.Add(Event(provisional.VoucherId, actorUserId, utcNow));
            discarded++;
        }

        // O comando em lote vem antes da leitura dos ids: o evento só vai para quem o UPDATE alcançou (D-12).
        var cancelled = await concessions.CancelAvailableVouchersAsync(campaignId, utcNow, cancellationToken);

        if (cancelled > 0)
        {
            foreach (var voucherId in await concessions.GetCancelledVoucherIdsAsync(campaignId, cancellationToken))
            {
                concessions.Add(Event(voucherId, actorUserId, utcNow));
            }
        }

        return new CampaignVoucherCancellation(cancelled, discarded, reversed);
    }

    private static VoucherEvent Event(Guid voucherId, Guid actorUserId, DateTime utcNow) => new()
    {
        Id = Guid.CreateVersion7(new DateTimeOffset(utcNow)),
        VoucherId = voucherId,
        EventType = VoucherEventTypes.Cancelled,
        OccurredAt = utcNow,
        ActorType = AuditActorType.User,
        ActorId = actorUserId,
        Payload = AuditJson.Serialize(new { Cause })
    };
}
