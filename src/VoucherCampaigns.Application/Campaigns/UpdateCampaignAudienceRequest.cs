using System.ComponentModel.DataAnnotations;

namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Público elegível (US-01.5): <c>AllCustomers</c> ou <c>Restricted</c>.</summary>
public record UpdateCampaignAudienceRequest(
    [Required(ErrorMessage = CampaignContract.AudienceTypeRequired)]
    string? AudienceType);
