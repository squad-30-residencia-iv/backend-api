using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Domain.Campaigns;

public class Campaign
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid NetworkId { get; set; }

    public Network? Network { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Início da vigência (UTC).</summary>
    public DateTime StartsAt { get; set; }

    /// <summary>Fim da vigência (UTC).</summary>
    public DateTime EndsAt { get; set; }

    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;

    /// <summary>Quantidade total de vouchers da campanha (RF06).</summary>
    public int? TotalVoucherLimit { get; set; }

    public VoucherValidityType? VoucherValidityType { get; set; }

    /// <summary>Validade relativa: dias a partir da emissão.</summary>
    public int? VoucherValidityDays { get; set; }

    /// <summary>Validade por data fixa (UTC).</summary>
    public DateTime? VoucherValidUntil { get; set; }

    /// <summary>Teto máximo da validade (UTC), opcional.</summary>
    public DateTime? VoucherValidityCap { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CampaignItem> Items { get; set; } = [];

    public ICollection<CampaignScope> Scopes { get; set; } = [];
}
