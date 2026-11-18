using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Domain.Vouchers;

/// <summary>Voucher emitido pelo motor (US-04.3). Nasce provisório e vira disponível na confirmação da venda (RF14c).</summary>
public class Voucher
{
    public Guid Id { get; set; }

    /// <summary>Código único (US-04.3, cenário 4); identificador interno da emissão.</summary>
    public string Code { get; set; } = string.Empty;

    public Guid NetworkId { get; set; }

    public Guid CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    /// <summary>Alvo que originou a concessão (US-04.3); o benefício do resgate vem dele.</summary>
    public Guid CampaignItemId { get; set; }

    public CampaignItem? Item { get; set; }

    public Guid ConcessionId { get; set; }

    public Concession? Concession { get; set; }

    /// <summary>CPF do cliente, só os 11 dígitos.</summary>
    public string CustomerDocument { get; set; } = string.Empty;

    public VoucherStatus Status { get; set; }

    /// <summary>Instante UTC da emissão.</summary>
    public DateTime IssuedAt { get; set; }

    /// <summary>Último instante de validade (UTC), inclusivo (RF18a, decisão D9).</summary>
    public DateTime ValidUntil { get; set; }

    /// <summary>Fim do prazo de reserva; null depois da confirmação ou do descarte.</summary>
    public DateTime? ReservedUntil { get; set; }

    /// <summary>Utilizações permitidas, copiadas da campanha na emissão (US-05.4).</summary>
    public int MaxUses { get; set; }

    public int UsesCount { get; set; }

    public Guid EstablishmentId { get; set; }

    public ChannelType Channel { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    public DateTime? DiscardedAt { get; set; }

    /// <summary>Instante UTC em que o cancelamento da campanha invalidou o voucher (RN10, spec GH-061 D-7).</summary>
    public DateTime? CancelledAt { get; set; }

    public ICollection<VoucherEvent> Events { get; set; } = [];

    public int RemainingUses => Math.Max(MaxUses - UsesCount, 0);

    /// <summary>
    /// Pode consumir uma utilização agora (spec GH-049): disponível dentro da validade com usos restantes, ou provisório
    /// com a reserva em dia quando a própria compra pode usá-lo (<paramref name="sameTransactionAllowed"/>, D-10).
    /// </summary>
    public bool CanBeRedeemedAt(DateTime utcNow, bool sameTransactionAllowed) =>
        RemainingUses > 0 && EffectiveStatus(utcNow) switch
        {
            VoucherStatus.Available => true,
            VoucherStatus.Provisional => sameTransactionAllowed,
            _ => false
        };

    /// <summary>Validade inclusiva até o último segundo (D9).</summary>
    public bool IsValidAt(DateTime utcNow) => BusinessTime.TruncateToSecond(utcNow) <= ValidUntil;

    /// <summary>
    /// Status na leitura: provisório com a reserva vencida já é descartado, e disponível com a validade vencida
    /// já é expirado, mesmo antes de o banco ser atualizado.
    /// </summary>
    public VoucherStatus EffectiveStatus(DateTime utcNow)
    {
        if (Status == VoucherStatus.Provisional && ReservedUntil is { } reservedUntil && utcNow >= reservedUntil)
        {
            return VoucherStatus.Discarded;
        }

        if (Status == VoucherStatus.Available && !IsValidAt(utcNow))
        {
            return VoucherStatus.Expired;
        }

        return Status;
    }
}
