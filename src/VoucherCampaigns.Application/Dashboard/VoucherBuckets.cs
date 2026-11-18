using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Application.Dashboard;

/// <summary>Em qual conta do acompanhamento o voucher entra no instante da leitura (spec GH-050, D-3).</summary>
public enum VoucherBucket
{
    /// <summary>Provisório com a reserva em dia: ainda não é emitido.</summary>
    Reserved,

    /// <summary>Provisório descartado ou com a reserva vencida: fora de todas as contas.</summary>
    Discarded,

    /// <summary>Emitido com ao menos uma utilização, qualquer que seja o status (usado em parte, vencido ou cancelado depois).</summary>
    Redeemed,

    /// <summary>Vencido sem nenhuma utilização.</summary>
    ExpiredUnused,

    /// <summary>Cancelado sem nenhuma utilização.</summary>
    CancelledUnused,

    /// <summary>Disponível, dentro da validade e sem utilização.</summary>
    Available
}

/// <summary>
/// A mesma regra que <c>DashboardRepository.CountVouchersAsync</c> aplica no SQL, para as leituras que agrupam em memória
/// (relatórios do portal). Os emitidos são a soma de <see cref="VoucherBucket.Redeemed"/>, <see cref="VoucherBucket.ExpiredUnused"/>,
/// <see cref="VoucherBucket.CancelledUnused"/> e <see cref="VoucherBucket.Available"/>.
/// </summary>
public static class VoucherBuckets
{
    public static VoucherBucket Of(VoucherStatus status, int usesCount, DateTime validUntil, DateTime? reservedUntil, DateTime utcNow)
    {
        if (status == VoucherStatus.Provisional)
        {
            return reservedUntil > utcNow ? VoucherBucket.Reserved : VoucherBucket.Discarded;
        }

        if (status == VoucherStatus.Discarded)
        {
            return VoucherBucket.Discarded;
        }

        if (usesCount > 0)
        {
            return VoucherBucket.Redeemed;
        }

        return status switch
        {
            VoucherStatus.Cancelled => VoucherBucket.CancelledUnused,
            VoucherStatus.Expired => VoucherBucket.ExpiredUnused,
            VoucherStatus.Available when validUntil < BusinessTime.TruncateToSecond(utcNow) => VoucherBucket.ExpiredUnused,
            VoucherStatus.Available => VoucherBucket.Available,
            _ => VoucherBucket.Redeemed
        };
    }

    /// <summary>Conta dos emitidos: tudo menos o provisório em dia e o descartado.</summary>
    public static bool IsIssued(this VoucherBucket bucket) =>
        bucket is not VoucherBucket.Reserved and not VoucherBucket.Discarded;
}
