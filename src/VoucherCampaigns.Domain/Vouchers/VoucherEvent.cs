using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Common;

namespace VoucherCampaigns.Domain.Vouchers;

/// <summary>Histórico imutável do voucher (RF21, RNF05, US-04.7). Só inclusão: o <c>AppDbContext</c> recusa alterar ou apagar.</summary>
public class VoucherEvent : IAppendOnly
{
    public Guid Id { get; set; }

    public Guid VoucherId { get; set; }

    public Voucher? Voucher { get; set; }

    /// <summary>Um dos <see cref="VoucherEventTypes"/>.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Instante UTC do evento.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>Quem causou o evento; null quando foi o próprio sistema (expiração da reserva).</summary>
    public AuditActorType? ActorType { get; set; }

    public Guid? ActorId { get; set; }

    /// <summary>Resumo em JSON curto; null quando não há o que resumir.</summary>
    public string? Payload { get; set; }
}
