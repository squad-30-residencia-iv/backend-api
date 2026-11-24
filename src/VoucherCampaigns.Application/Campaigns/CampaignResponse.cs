using VoucherCampaigns.Application.Customers;
using VoucherCampaigns.Application.Eligibility;

namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Campanha; datas com o offset do fuso da rede.</summary>
/// <param name="TotalVoucherLimit">Quantidade total de vouchers (RF06); null enquanto não configurada (US-01.3).</param>
/// <param name="PerCustomerVoucherLimit">Limite por cliente (RF06c); null = sem limite.</param>
/// <param name="UndistributedVouchers">Total menos a soma das cotas por alvo; null sem total definido.</param>
/// <param name="IssuedVouchers">Vouchers confirmados pelo motor (EP-04).</param>
/// <param name="ReservedVouchers">Vouchers provisórios vivos, que contam para o esgotamento (US-04.9).</param>
/// <param name="AudienceType">Público elegível (US-01.5); null enquanto não definido.</param>
/// <param name="VoucherValidity">Validade do voucher (US-01.12); null enquanto não definida.</param>
/// <param name="MinPurchaseAmount">Valor mínimo da compra para o uso (US-01.12); null = sem mínimo.</param>
/// <param name="MaxPurchaseAmount">Valor máximo da compra para o uso (US-01.12); null = sem máximo.</param>
/// <param name="MaxUsesPerVoucher">Utilizações por voucher (US-01.12); null enquanto não definida.</param>
/// <param name="Conditions">Condições de geração (US-01.4), na ordem informada.</param>
/// <param name="AllowUseInSamePurchase">Uso na mesma compra que gerou o voucher (US-01.4); falso por padrão.</param>
/// <param name="RejectionReason">Motivo da reprovação (US-01.11); nulo quando a campanha não foi reprovada.</param>
/// <param name="Scopes">Onde a campanha vale (US-02.3), na ordem informada; vazio enquanto não definido.</param>
/// <param name="RedeemedVouchers">Vouchers emitidos com ao menos uma utilização, a mesma conta do dashboard (spec GH-050, D-3).</param>
/// <param name="ExpiredVouchers">Vouchers vencidos sem nenhuma utilização, a mesma conta do dashboard (spec GH-050, D-3).</param>
/// <param name="Eligibility">Critérios do público restrito (spec GH-047, D-10); null no público "todos" ou não definido.</param>
/// <param name="RedemptionTargets">Onde o voucher pode ser usado (spec GH-070, D-2); vazio = no alvo da concessão.</param>
/// <param name="RedemptionWindows">Dias e horários do resgate, no fuso da rede (spec GH-070, D-3); vazio = sempre.</param>
public record CampaignResponse(
    Guid Id,
    Guid NetworkId,
    string Name,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string TimeZone,
    string Status,
    int? TotalVoucherLimit,
    int? PerCustomerVoucherLimit,
    int? UndistributedVouchers,
    int IssuedVouchers,
    IReadOnlyList<CampaignItemResponse> Items,
    DateTime CreatedAt,
    string? AudienceType,
    VoucherValidityResponse? VoucherValidity,
    decimal? MinPurchaseAmount,
    decimal? MaxPurchaseAmount,
    int? MaxUsesPerVoucher,
    IReadOnlyList<CampaignConditionResponse> Conditions,
    bool AllowUseInSamePurchase,
    string? RejectionReason,
    IReadOnlyList<CampaignScopeResponse> Scopes,
    int ReservedVouchers,
    int RedeemedVouchers,
    int ExpiredVouchers,
    CampaignEligibilityResponse? Eligibility = null,
    IReadOnlyList<RedemptionTargetResponse>? RedemptionTargets = null,
    IReadOnlyList<RedemptionWindowResponse>? RedemptionWindows = null,
    IReadOnlyList<CampaignCustomerGroupResponse>? CustomerGroups = null,
    int UniqueCustomerCount = 0);

/// <summary>Alvo de resgate (spec GH-070, D-2), com o código do produto quando o alvo é um produto.</summary>
public record RedemptionTargetResponse(Guid Id, string TargetType, Guid? CategoryId, Guid? ProductId, string? ProductCode);

/// <summary>Janela de resgate (spec GH-070, D-3): dias em que começa e horário <c>HH:mm</c> no fuso da rede.</summary>
public record RedemptionWindowResponse(IReadOnlyList<string> DaysOfWeek, string Start, string End);

/// <summary>Local do escopo: região ou loja (as duas nulas = rede inteira), com os canais em ordem fixa.</summary>
/// <param name="Name">Nome da região ou da loja; nulo para a rede inteira.</param>
/// <param name="Code">Código da loja; nulo para região e rede.</param>
public record CampaignScopeResponse(
    Guid? RegionId,
    Guid? EstablishmentId,
    string? Name,
    string? Code,
    IReadOnlyList<string> Channels);

public record CampaignItemResponse(
    Guid Id,
    string TargetType,
    Guid? CategoryId,
    Guid? ProductId,
    string BenefitType,
    decimal BenefitValue,
    int? VoucherLimit,
    string? ProductCode);

/// <summary>Validade do voucher; <c>Amount</c>, <c>Unit</c> e <c>Cap</c> só no <c>Relative</c>, <c>ValidUntil</c> só no <c>FixedDate</c>.</summary>
public record VoucherValidityResponse(
    string Type,
    int? Amount,
    string? Unit,
    DateTimeOffset? ValidUntil,
    DateTimeOffset? Cap);

public record CampaignConditionResponse(
    Guid Id,
    string Type,
    decimal? MinAmount,
    int? MinQuantity,
    Guid? CategoryId,
    Guid? ProductId);
