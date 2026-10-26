using System.ComponentModel.DataAnnotations;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Common;

namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Cadastro de campanha (US-01.1). Datas na hora local da rede, sem fuso.</summary>
public record CreateCampaignRequest(
    [Required(ErrorMessage = CampaignContract.NetworkRequired)]
    Guid? NetworkId,
    [Required(ErrorMessage = "Informe o nome da campanha.")]
    [StringLength(150, ErrorMessage = "O nome deve ter no máximo 150 caracteres.")]
    string Name,
    [StringLength(500, ErrorMessage = "A descrição deve ter no máximo 500 caracteres.")]
    string? Description,
    [Required(ErrorMessage = "Informe a data e hora de início.")]
    [LocalDateTime]
    DateTime? StartsAt,
    [Required(ErrorMessage = "Informe a data e hora de fim.")]
    [LocalDateTime]
    DateTime? EndsAt)
    : INetworkScopedRequest
{
    // O TenantGuard roda depois da validação do modelo, quando NetworkId já é obrigatório.
    Guid INetworkScopedRequest.NetworkId => NetworkId ?? Guid.Empty;
}
