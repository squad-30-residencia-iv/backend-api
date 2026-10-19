namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Campanha cadastrada; datas com o offset do fuso da rede.</summary>
public record CampaignResponse(
    Guid Id,
    Guid NetworkId,
    string Name,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string TimeZone,
    string Status,
    DateTime CreatedAt);
