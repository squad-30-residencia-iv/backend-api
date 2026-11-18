using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Domain.Common;

namespace VoucherCampaigns.Application.Audit;

public class AuditQueryService(
    IAuditQueryRepository audit,
    INetworkRepository networks,
    ICurrentUser currentUser) : IAuditQueryService
{
    public async Task<IReadOnlyList<AuditEntryListItemResponse>> ListAsync(
        Guid? networkId,
        string? eventType,
        Guid? campaignId,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken)
    {
        var scopedNetworkId = InputValues.RequiredId(networkId, "NetworkId", AuditQueryContract.NetworkRequired);
        NetworkScope.Ensure(currentUser, scopedNetworkId);

        var network = await networks.FindByIdAsync(scopedNetworkId, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.NetworkNotFound);

        var timeZone = NetworkTime.Resolve(network.TimeZone);
        var fromUtc = from is null ? (DateTime?)null : NetworkTime.ToUtc(from.Value.Date, timeZone, "from");
        var toUtcExclusive = to is null
            ? (DateTime?)null
            : NetworkTime.ToUtc(to.Value.Date.AddDays(1), timeZone, "to");

        var rows = await audit.ListAsync(
            scopedNetworkId,
            string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim(),
            campaignId,
            fromUtc,
            toUtcExclusive,
            cancellationToken);

        return rows.Select(row => MapListItem(row, timeZone)).ToList();
    }

    public async Task<AuditEntryDetailResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await audit.FindByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(AuditQueryContract.EntryNotFound);

        if (row.NetworkId is null)
        {
            throw new ResourceNotFoundException(AuditQueryContract.EntryNotFound);
        }

        NetworkScope.Ensure(currentUser, row.NetworkId.Value);

        var timeZone = NetworkTime.Resolve(row.NetworkTimeZone ?? "America/Sao_Paulo");

        return new AuditEntryDetailResponse(
            row.Id,
            row.EventType,
            row.ActorType,
            row.ActorId,
            row.NetworkId,
            row.CampaignId,
            row.CampaignName,
            row.EntityType,
            row.EntityId,
            NetworkTime.ToLocal(row.OccurredAt, timeZone),
            row.Payload);
    }

    private static AuditEntryListItemResponse MapListItem(AuditEntryListRow row, TimeZoneInfo timeZone) =>
        new(
            row.Id,
            row.EventType,
            row.ActorType,
            row.ActorId,
            row.CampaignId,
            row.CampaignName,
            row.EntityType,
            row.EntityId,
            NetworkTime.ToLocal(row.OccurredAt, timeZone),
            row.Payload);
}
