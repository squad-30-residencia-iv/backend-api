using System.ComponentModel.DataAnnotations;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Common;

namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Cadastro de campanha (US-01.1). Datas na hora local da rede, sem fuso.</summary>
public record CreateCampaignRequest(
    [Required(ErrorMessage = CampaignContract.NetworkRequired)]
    Guid? NetworkId,
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
    DateTime? EndsAt)
    : INetworkScopedRequest
{
    // O TenantGuard roda depois da validação do modelo, quando NetworkId já é obrigatório.
    Guid INetworkScopedRequest.NetworkId => NetworkId ?? Guid.Empty;
}
