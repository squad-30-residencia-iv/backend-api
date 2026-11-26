namespace VoucherCampaigns.Domain.Campaigns;

/// <summary>
/// Tag da rede para agrupar campanhas (Black Friday, Dia das Mães), spec GH-077 D-1. O administrador mantém a lista;
/// a tag não se apaga, só inativa, para as campanhas que já a usam continuarem com o nome.
/// </summary>
public class CampaignTag
{
    public const int NameMaxLength = 40;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid NetworkId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Nome em maiúsculas e sem espaços sobrando: a unicidade na rede não diferencia maiúsculas.</summary>
    public string NormalizedName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();

    public void Rename(string name)
    {
        Name = name.Trim();
        NormalizedName = Normalize(name);
    }
}

/// <summary>Tag aplicada a uma campanha (spec GH-077, D-2).</summary>
public class CampaignTagLink
{
    public Guid Id { get; set; }

    public Guid CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    public Guid TagId { get; set; }

    public CampaignTag? Tag { get; set; }
}
