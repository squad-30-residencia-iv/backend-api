namespace VoucherCampaigns.Application.Audit;

public record AuditEntryListItemResponse(
    Guid Id,
    string EventType,
    string ActorType,
    Guid ActorId,
    Guid? CampaignId,
    string? CampaignName,
    string EntityType,
    Guid EntityId,
    DateTimeOffset OccurredAt,
    string? Payload);

public record AuditEntryDetailResponse(
    Guid Id,
    string EventType,
    string ActorType,
    Guid ActorId,
    Guid? NetworkId,
    Guid? CampaignId,
    string? CampaignName,
    string EntityType,
    Guid EntityId,
    DateTimeOffset OccurredAt,
    string? Payload);

public static class AuditQueryContract
{
    public const string NetworkRequired = "Informe a rede.";
    public const string EntryNotFound = "Registro de auditoria não encontrado.";
}
