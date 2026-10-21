namespace VoucherCampaigns.Domain.Organization;

/// <summary>Rede: topo da hierarquia organizacional.</summary>
public class Network
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public const string DefaultTimeZone = "America/Sao_Paulo";

    public string Name { get; set; } = string.Empty;

    /// <summary>Fuso IANA em que as datas da rede são interpretadas (US-02.6, ex.: vigência da campanha).</summary>
    public string TimeZone { get; set; } = DefaultTimeZone;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Region> Regions { get; set; } = [];
}
