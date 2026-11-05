using System.ComponentModel.DataAnnotations;
using VoucherCampaigns.Application.Common;

namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Edição dos dados básicos em rascunho (US-01.7). Datas na hora local da rede, sem fuso.</summary>
public record UpdateCampaignRequest(
    [Required(ErrorMessage = CampaignContract.NameRequired)]
    [StringLength(150, ErrorMessage = CampaignContract.NameTooLong)]
    string Name,
    [StringLength(500, ErrorMessage = CampaignContract.DescriptionTooLong)]
    string? Description,
    [Required(ErrorMessage = CampaignContract.StartsAtRequired)]
    [LocalDateTime]
    DateTime? StartsAt,
    [Required(ErrorMessage = CampaignContract.EndsAtRequired)]
    [LocalDateTime]
    DateTime? EndsAt);
