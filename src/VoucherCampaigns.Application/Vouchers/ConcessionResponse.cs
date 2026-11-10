namespace VoucherCampaigns.Application.Vouchers;

/// <summary>Resultado da concessão (US-07.2). Datas com o offset do fuso da rede.</summary>
/// <param name="Status"><c>NotGranted</c>, <c>Provisional</c>, <c>Confirmed</c>, <c>Cancelled</c> ou <c>Expired</c> (efetivo).</param>
/// <param name="Reason">Motivo da não concessão; só no <c>NotGranted</c>.</param>
/// <param name="ReservedUntil">Fim do prazo de reserva; só no <c>Provisional</c>.</param>
/// <param name="Voucher">Voucher emitido; null quando não houve concessão.</param>
public record ConcessionResponse(
    Guid ConcessionId,
    string TransactionId,
    string Status,
    string? Reason,
    DateTimeOffset? ReservedUntil,
    VoucherResponse? Voucher);

/// <summary>Voucher como a aplicação satélite o vê (US-04.8, US-07.4).</summary>
/// <param name="Status">Status efetivo: <c>Provisional</c>, <c>Available</c>, <c>Redeemed</c>, <c>Expired</c>, <c>Cancelled</c> ou <c>Discarded</c>.</param>
/// <param name="ValidUntil">Último instante de validade, inclusivo.</param>
/// <param name="AllowUseInSamePurchase">Se a campanha permite usar o voucher na compra que o gerou (US-01.4).</param>
public record VoucherResponse(
    Guid Id,
    string Code,
    Guid CampaignId,
    string CampaignName,
    string Status,
    DateTimeOffset IssuedAt,
    DateTimeOffset ValidUntil,
    int MaxUses,
    int RemainingUses,
    bool AllowUseInSamePurchase,
    VoucherTargetResponse Target,
    Guid ConcessionId,
    string TransactionId);

/// <summary>Alvo que originou o voucher e o benefício dele (US-01.2).</summary>
public record VoucherTargetResponse(
    string TargetType,
    Guid? CategoryId,
    Guid? ProductId,
    string BenefitType,
    decimal BenefitValue);
