using System.Linq.Expressions;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Infrastructure.Persistence.Repositories;

/// <summary>
/// O status efetivo (PR-04) traduzido para predicados SQL (spec GH-071, D-5), para o filtro e a página saírem do banco
/// já com o mesmo resultado de <see cref="Campaign.EffectiveStatus"/> e <see cref="Voucher.EffectiveStatus"/>.
/// </summary>
internal static class EffectiveStatusFilters
{
    private static readonly CampaignStatus[] Live = [CampaignStatus.Scheduled, CampaignStatus.Active, CampaignStatus.Paused];
    private static readonly CampaignStatus[] Running = [CampaignStatus.Scheduled, CampaignStatus.Active];

    public static Expression<Func<Campaign, bool>> Campaign(CampaignStatus status, DateTime utcNow)
    {
        // D9: o minuto do fim conta inteiro; passou do fim quando agora >= fim + 1 minuto, ou seja, fim <= agora - 1 minuto.
        var endCut = utcNow.AddMinutes(-1);

        return status switch
        {
            CampaignStatus.Ended => campaign => campaign.Status == CampaignStatus.Ended
                || (Live.Contains(campaign.Status) && campaign.EndsAt <= endCut),
            CampaignStatus.Paused => campaign => campaign.Status == CampaignStatus.Paused && campaign.EndsAt > endCut,
            CampaignStatus.Scheduled => campaign => Running.Contains(campaign.Status) && campaign.EndsAt > endCut
                && utcNow < campaign.StartsAt,
            CampaignStatus.Active => campaign => Running.Contains(campaign.Status) && campaign.EndsAt > endCut
                && campaign.StartsAt <= utcNow
                && (campaign.TotalVoucherLimit == null || campaign.IssuedVouchers + campaign.ReservedVouchers < campaign.TotalVoucherLimit),
            CampaignStatus.SoldOut => campaign => Running.Contains(campaign.Status) && campaign.EndsAt > endCut
                && campaign.StartsAt <= utcNow
                && campaign.TotalVoucherLimit != null && campaign.IssuedVouchers + campaign.ReservedVouchers >= campaign.TotalVoucherLimit,
            _ => campaign => campaign.Status == status
        };
    }

    public static Expression<Func<Voucher, bool>> Voucher(VoucherStatus status, DateTime utcNow)
    {
        // Voucher.IsValidAt compara a validade com o agora truncado ao segundo.
        var now = BusinessTime.TruncateToSecond(utcNow);

        return status switch
        {
            VoucherStatus.Discarded => voucher => voucher.Status == VoucherStatus.Discarded
                || (voucher.Status == VoucherStatus.Provisional && voucher.ReservedUntil != null && voucher.ReservedUntil <= utcNow),
            VoucherStatus.Provisional => voucher => voucher.Status == VoucherStatus.Provisional
                && (voucher.ReservedUntil == null || voucher.ReservedUntil > utcNow),
            VoucherStatus.Expired => voucher => voucher.Status == VoucherStatus.Expired
                || (voucher.Status == VoucherStatus.Available && voucher.ValidUntil < now),
            VoucherStatus.Available => voucher => voucher.Status == VoucherStatus.Available && voucher.ValidUntil >= now,
            _ => voucher => voucher.Status == status
        };
    }

    /// <summary>Tudo menos o descartado: a listagem do portal sem filtro de status.</summary>
    public static Expression<Func<Voucher, bool>> NotDiscarded(DateTime utcNow) =>
        voucher => voucher.Status != VoucherStatus.Discarded
            && !(voucher.Status == VoucherStatus.Provisional && voucher.ReservedUntil != null && voucher.ReservedUntil <= utcNow);
}
