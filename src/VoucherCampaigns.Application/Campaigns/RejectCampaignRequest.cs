using System.ComponentModel.DataAnnotations;

namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Reprovação da campanha em aprovação (US-01.11). Motivo opcional.</summary>
public record RejectCampaignRequest(
    [StringLength(500, ErrorMessage = CampaignContract.RejectionReasonTooLong)]
    string? Reason);
