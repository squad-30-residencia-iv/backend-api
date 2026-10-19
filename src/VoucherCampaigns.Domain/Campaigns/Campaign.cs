using VoucherCampaigns.Domain.Common;
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

    /// <summary>
    /// Cria a campanha em rascunho (PR-01) aplicando a RN02. Datas em UTC; o "agora" é comparado
    /// na precisão de minuto, para um início igual ao minuto atual ser aceito.
    /// </summary>
    /// <exception cref="BusinessRuleException">Período inválido ou início no passado.</exception>
    public static Campaign CreateDraft(
        Guid networkId,
        string name,
        string? description,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        DateTime utcNow)
    {
        EnsureUtc(startsAtUtc, nameof(startsAtUtc));
        EnsureUtc(endsAtUtc, nameof(endsAtUtc));
        EnsureUtc(utcNow, nameof(utcNow));

        // Início igual ao fim também é recusado: o banco exige ends_at > starts_at e a vigência ficaria vazia.
        if (startsAtUtc >= endsAtUtc)
        {
            throw new BusinessRuleException(CampaignErrors.InvalidPeriodCode, CampaignErrors.InvalidPeriodMessage);
        }

        if (startsAtUtc < TruncateToMinute(utcNow))
        {
            throw new BusinessRuleException(CampaignErrors.StartInPastCode, CampaignErrors.StartInPastMessage);
        }

        return new Campaign
        {
            NetworkId = networkId,
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            StartsAt = startsAtUtc,
            EndsAt = endsAtUtc,
            Status = CampaignStatus.Draft,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };
    }

    /// <summary>Só campanha ativa e dentro da vigência concede voucher (PR-10); rascunho nunca concede.</summary>
    public bool CanIssueVouchers(DateTime utcNow) =>
        Status == CampaignStatus.Active && StartsAt <= utcNow && utcNow < EndsAt;

    private static DateTime TruncateToMinute(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMinute, value.Kind);

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("A data deve estar em UTC.", parameterName);
        }
    }
}
