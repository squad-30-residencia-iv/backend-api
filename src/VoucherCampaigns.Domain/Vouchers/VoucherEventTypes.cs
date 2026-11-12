namespace VoucherCampaigns.Domain.Vouchers;

/// <summary>Tipos de evento do histórico do voucher (RF21, US-04.7). O resgate (EP-05) acrescenta os seus.</summary>
public static class VoucherEventTypes
{
    public const string Issued = "Issued";
    public const string Confirmed = "Confirmed";
    public const string Cancelled = "Cancelled";
    public const string ReservationExpired = "ReservationExpired";

    /// <summary>Utilização consumida numa compra (US-05.4, spec GH-049).</summary>
    public const string Redeemed = "Redeemed";

    /// <summary>Resgate estornado: a utilização voltou (GH-049, D-19).</summary>
    public const string RedemptionReversed = "RedemptionReversed";

    /// <summary>Validade própria vencida, gravada na tentativa de resgate (US-05.3, cenário 2).</summary>
    public const string Expired = "Expired";
}
