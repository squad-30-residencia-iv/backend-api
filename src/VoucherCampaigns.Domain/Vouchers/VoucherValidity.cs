using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;

namespace VoucherCampaigns.Domain.Vouchers;

/// <summary>Validade própria do voucher, calculada na emissão (RF18a, US-04.3, US-01.12).</summary>
public static class VoucherValidity
{
    /// <summary>
    /// Prazo relativo: emissão mais N dias ou horas, limitado ao teto quando houver. Data fixa: a data da campanha.
    /// Data fixa e teto são informados ao minuto e valem até o último segundo dele (D9).
    /// </summary>
    /// <exception cref="InvalidOperationException">Campanha sem validade configurada (a submissão exige).</exception>
    public static DateTime Compute(Campaign campaign, DateTime issuedAtUtc)
    {
        switch (campaign.VoucherValidityType)
        {
            case Campaigns.VoucherValidityType.FixedDate:
                return BusinessTime.LastSecondOfMinute(campaign.VoucherValidUntil
                    ?? throw new InvalidOperationException("A campanha com validade por data fixa não tem a data."));

            case Campaigns.VoucherValidityType.Relative:
                var amount = campaign.VoucherValidityAmount
                    ?? throw new InvalidOperationException("A campanha com validade relativa não tem o prazo.");
                var validUntil = campaign.VoucherValidityUnit == VoucherValidityUnit.Hours
                    ? issuedAtUtc.AddHours(amount)
                    : issuedAtUtc.AddDays(amount);

                if (campaign.VoucherValidityCap is { } cap)
                {
                    var capEnd = BusinessTime.LastSecondOfMinute(cap);
                    validUntil = capEnd < validUntil ? capEnd : validUntil;
                }

                return validUntil;

            default:
                throw new InvalidOperationException("A campanha não tem a validade do voucher configurada.");
        }
    }
}
