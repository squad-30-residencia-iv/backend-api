using System.ComponentModel.DataAnnotations;

namespace VoucherCampaigns.Application.Campaigns;

/// <summary>
/// Lista completa de alvos de resgate (spec GH-070, D-2); substitui a atual. Vazia = o voucher vale no alvo da
/// concessão, como antes.
/// </summary>
public record ReplaceRedemptionTargetsRequest(
    [Required(ErrorMessage = CampaignContract.RedemptionTargetsRequired)]
    [MaxLength(CampaignContract.MaxItems, ErrorMessage = CampaignContract.RedemptionTargetsTooMany)]
    IReadOnlyList<RedemptionTargetRequest?>? Targets);

/// <summary>Categoria (<c>CategoryId</c>) ou produto (<c>ProductId</c> ou <c>ProductCode</c>, nunca os dois).</summary>
public record RedemptionTargetRequest(
    [Required(ErrorMessage = CampaignContract.TargetTypeRequired)]
    string? TargetType,
    Guid? CategoryId,
    Guid? ProductId,
    [StringLength(CampaignContract.MaxProductCodeLength, ErrorMessage = CampaignContract.TargetProductCodeTooLong)]
    string? ProductCode = null);

/// <summary>Lista completa de janelas de resgate (spec GH-070, D-3); substitui a atual. Vazia = qualquer dia e hora.</summary>
public record ReplaceRedemptionWindowsRequest(
    [Required(ErrorMessage = CampaignContract.RedemptionWindowsRequired)]
    [MaxLength(CampaignContract.MaxRedemptionWindows, ErrorMessage = CampaignContract.RedemptionWindowsTooMany)]
    IReadOnlyList<RedemptionWindowRequest?>? Windows);

/// <summary>
/// Dias em que a janela começa e horário <c>HH:mm</c> no fuso da rede; o fim vale até o último segundo do minuto.
/// Fim antes do início = a janela vira a noite e termina no dia seguinte.
/// </summary>
public record RedemptionWindowRequest(IReadOnlyList<string?>? DaysOfWeek, string? Start, string? End);
