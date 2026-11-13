using System.ComponentModel.DataAnnotations;
using VoucherCampaigns.Application.Eligibility;

namespace VoucherCampaigns.Application.Campaigns;

/// <summary>
/// Público elegível (US-01.5): <c>AllCustomers</c>, sem critérios, ou <c>Restricted</c>, com os critérios que valem
/// juntos (spec GH-047, D-6 e D-9). Substitui o público inteiro.
/// </summary>
public record UpdateCampaignAudienceRequest(
    [Required(ErrorMessage = CampaignContract.AudienceTypeRequired)]
    string? AudienceType,
    [MaxLength(EligibilityContract.MaxCriteria, ErrorMessage = EligibilityContract.CriteriaTooMany)]
    IReadOnlyList<EligibilityCriterionRequest?>? Criteria = null);
