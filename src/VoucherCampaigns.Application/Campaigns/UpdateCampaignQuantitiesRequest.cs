using System.ComponentModel.DataAnnotations;

namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Quantidade total e limite por cliente (US-01.3). Limite nulo = sem limite por cliente.</summary>
public record UpdateCampaignQuantitiesRequest(
    [Required(ErrorMessage = CampaignContract.TotalVoucherLimitRequired)]
    [Range(1, int.MaxValue, ErrorMessage = CampaignContract.TotalVoucherLimitPositive)]
    int? TotalVoucherLimit,
    [Range(1, int.MaxValue, ErrorMessage = CampaignContract.PerCustomerVoucherLimitPositive)]
    int? PerCustomerVoucherLimit);
