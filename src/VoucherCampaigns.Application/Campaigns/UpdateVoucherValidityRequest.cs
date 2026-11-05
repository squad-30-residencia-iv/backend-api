using System.ComponentModel.DataAnnotations;
using VoucherCampaigns.Application.Common;

namespace VoucherCampaigns.Application.Campaigns;

/// <summary>
/// Validade do voucher (US-01.12): <c>Relative</c> usa <c>Amount</c>, <c>Unit</c> e o teto opcional <c>Cap</c>;
/// <c>FixedDate</c> usa só <c>ValidUntil</c>. Datas na hora local da rede, sem fuso.
/// </summary>
public record UpdateVoucherValidityRequest(
    [Required(ErrorMessage = CampaignContract.ValidityTypeRequired)]
    string? Type,
    int? Amount,
    string? Unit,
    [LocalDateTime]
    DateTime? Cap,
    [LocalDateTime]
    DateTime? ValidUntil);
