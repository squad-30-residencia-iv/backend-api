namespace VoucherCampaigns.Application.Authentication;

/// <summary>Escopos das aplicações satélites (US-07.5), com o valor exato gravado no token.</summary>
public static class SatelliteScopes
{
    /// <summary>Consultar campanhas vigentes (US-07.1).</summary>
    public const string ReadCampaigns = "campaigns:read";

    /// <summary>Conceder voucher (US-07.2, US-07.4).</summary>
    public const string IssueVouchers = "vouchers:issue";

    /// <summary>Resgatar voucher (US-07.3).</summary>
    public const string RedeemVouchers = "vouchers:redeem";

    public static readonly IReadOnlyList<string> All = [ReadCampaigns, IssueVouchers, RedeemVouchers];
}
