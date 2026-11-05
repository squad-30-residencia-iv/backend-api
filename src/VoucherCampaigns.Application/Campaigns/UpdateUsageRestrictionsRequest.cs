using System.ComponentModel.DataAnnotations;

namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Restrições de uso no resgate (US-01.12). Valores de compra nulos = sem limite.</summary>
public record UpdateUsageRestrictionsRequest(
    decimal? MinPurchaseAmount,
    decimal? MaxPurchaseAmount,
    [Required(ErrorMessage = CampaignContract.MaxUsesRequired)]
    [Range(1, 100, ErrorMessage = CampaignContract.MaxUsesRange)]
    int? MaxUsesPerVoucher);
