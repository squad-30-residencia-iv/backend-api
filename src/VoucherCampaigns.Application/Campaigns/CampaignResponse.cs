namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Campanha; datas com o offset do fuso da rede.</summary>
/// <param name="TotalVoucherLimit">Quantidade total de vouchers (RF06); null enquanto não configurada (US-01.3).</param>
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
    int IssuedVouchers,
    DateTime CreatedAt);
