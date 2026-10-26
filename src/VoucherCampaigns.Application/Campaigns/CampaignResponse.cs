namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Campanha; datas com o offset do fuso da rede.</summary>
/// <param name="TotalVoucherLimit">Quantidade total de vouchers (RF06); null enquanto não configurada (US-01.3).</param>
/// <param name="PerCustomerVoucherLimit">Limite por cliente (RF06c); null = sem limite.</param>
/// <param name="UndistributedVouchers">Total menos a soma das cotas por alvo; null sem total definido.</param>
/// <param name="IssuedVouchers">Vouchers emitidos; sempre 0 até a emissão existir (EP-04).</param>
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
    DateTime CreatedAt);

public record CampaignItemResponse(
    Guid Id,
    string TargetType,
    Guid? CategoryId,
    Guid? ProductId,
    string BenefitType,
    decimal BenefitValue,
    int? VoucherLimit);
