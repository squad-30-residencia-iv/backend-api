namespace VoucherCampaigns.Application.Audit;

public interface IAuditQueryService
{
    Task<IReadOnlyList<AuditEntryListItemResponse>> ListAsync(
        Guid? networkId,
        string? eventType,
        Guid? campaignId,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken);

    Task<AuditEntryDetailResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}

public interface IAuditQueryRepository
{
    Task<IReadOnlyList<AuditEntryListRow>> ListAsync(
        Guid networkId,
        string? eventType,
        Guid? campaignId,
        DateTime? fromUtc,
        DateTime? toUtcExclusive,
        CancellationToken cancellationToken);

    Task<AuditEntryListRow?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record AuditEntryListRow(
    Guid Id,
    string EventType,
    string ActorType,
    Guid ActorId,
    Guid? NetworkId,
    Guid? CampaignId,
    string? CampaignName,
    string EntityType,
    Guid EntityId,
    DateTime OccurredAt,
    string? Payload,
    string? NetworkTimeZone);
