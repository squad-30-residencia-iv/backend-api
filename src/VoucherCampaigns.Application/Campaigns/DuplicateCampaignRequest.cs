using System.ComponentModel.DataAnnotations;
using VoucherCampaigns.Application.Common;

namespace VoucherCampaigns.Application.Campaigns;

/// <summary>
/// Duplicação da campanha (US-01.10). Tudo opcional: sem nome, "Cópia de {nome}"; sem data, a da origem.
/// Datas na hora local da rede, sem fuso.
/// </summary>
public record DuplicateCampaignRequest(
    [StringLength(CampaignContract.MaxNameLength, ErrorMessage = CampaignContract.NameTooLong)]
    string? Name,
    [LocalDateTime]
    DateTime? StartsAt,
    [LocalDateTime]
    DateTime? EndsAt);
