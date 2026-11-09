using VoucherCampaigns.Domain.Common;

namespace VoucherCampaigns.Domain.Audit;

/// <summary>Registro da trilha de auditoria (RF27a). Só inclusão: não é alterado nem apagado.</summary>
public class AuditEntry : IAppendOnly
{
    public Guid Id { get; set; }

    /// <summary>Um dos <see cref="AuditEventTypes"/>.</summary>
    public string EventType { get; set; } = string.Empty;

    public AuditActorType ActorType { get; set; }

    public Guid ActorId { get; set; }

    public Guid? NetworkId { get; set; }

    public Guid? CampaignId { get; set; }

    /// <summary>Tipo do registro afetado, ex.: <c>Campaign</c>.</summary>
    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    /// <summary>Instante UTC da ação.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>Resumo da ação em JSON; null quando não há o que resumir.</summary>
    public string? Payload { get; set; }
}
