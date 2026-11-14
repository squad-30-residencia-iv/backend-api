using System.ComponentModel.DataAnnotations;

namespace VoucherCampaigns.Application.Campaigns;

/// <summary>
/// Cancelamento da campanha (US-01.8, RN10; spec GH-061 D-4). Motivo obrigatório, até 500 caracteres depois do
/// trim: o limite é conferido no serviço, não aqui.
/// </summary>
public record CancelCampaignRequest(
    [Required(ErrorMessage = CampaignContract.CancellationReasonRequired)]
    string? Reason);
