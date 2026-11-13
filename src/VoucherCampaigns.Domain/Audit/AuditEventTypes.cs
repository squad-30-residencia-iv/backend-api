namespace VoucherCampaigns.Domain.Audit;

/// <summary>Tipos de evento da trilha de auditoria. O motor (issue #27) acrescenta os de emissão e resgate.</summary>
public static class AuditEventTypes
{
    /// <summary>Criação do rascunho e cada edição de uma seção da configuração (spec GH-050, D-8).</summary>
    public const string CampaignCreated = "CampaignCreated";
    public const string CampaignUpdated = "CampaignUpdated";

    public const string CampaignSubmitted = "CampaignSubmitted";
    public const string CampaignApproved = "CampaignApproved";
    public const string CampaignRejected = "CampaignRejected";
    public const string CampaignPaused = "CampaignPaused";
    public const string CampaignEnded = "CampaignEnded";
    public const string CampaignCancelled = "CampaignCancelled";
    public const string CampaignDuplicated = "CampaignDuplicated";

    /// <summary>Motor de concessão (spec GH-045, D-20); autor <see cref="AuditActorType.Application"/>.</summary>
    public const string VoucherIssued = "VoucherIssued";
    public const string VoucherConfirmed = "VoucherConfirmed";
    public const string VoucherCancelled = "VoucherCancelled";
    public const string VoucherIssuanceRefused = "VoucherIssuanceRefused";

    /// <summary>Resgate (spec GH-049): utilização consumida, recusa com o motivo e estorno.</summary>
    public const string VoucherRedeemed = "VoucherRedeemed";
    public const string VoucherRedemptionRefused = "VoucherRedemptionRefused";
    public const string VoucherRedemptionReversed = "VoucherRedemptionReversed";
}
