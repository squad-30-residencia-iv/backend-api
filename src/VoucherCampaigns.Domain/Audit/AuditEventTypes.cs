namespace VoucherCampaigns.Domain.Audit;

/// <summary>Tipos de evento da trilha de auditoria. O motor (issue #27) acrescenta os de emissão e resgate.</summary>
public static class AuditEventTypes
{
    public const string CampaignSubmitted = "CampaignSubmitted";
    public const string CampaignApproved = "CampaignApproved";
    public const string CampaignRejected = "CampaignRejected";
    public const string CampaignPaused = "CampaignPaused";
    public const string CampaignEnded = "CampaignEnded";
    public const string CampaignDuplicated = "CampaignDuplicated";
}
