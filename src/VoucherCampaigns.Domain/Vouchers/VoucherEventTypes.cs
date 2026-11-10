namespace VoucherCampaigns.Domain.Vouchers;

/// <summary>Tipos de evento do histórico do voucher (RF21, US-04.7). O resgate (EP-05) acrescenta os seus.</summary>
public static class VoucherEventTypes
{
    public const string Issued = "Issued";
    public const string Confirmed = "Confirmed";
    public const string Cancelled = "Cancelled";
    public const string ReservationExpired = "ReservationExpired";
}
