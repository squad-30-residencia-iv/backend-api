using System.Globalization;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Application.Vouchers;

public class PortalVoucherQueryService(
    IPortalVoucherReadRepository vouchers,
    INetworkRepository networks,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IPortalVoucherQueryService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private const int CpfLength = 11;

    public async Task<PagedResult<PortalVoucherListItemResponse>> ListAsync(
        Guid? networkId,
        Guid? campaignId,
        string? status,
        string? search,
        DateTime? issuedFrom,
        DateTime? issuedTo,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var scopedNetworkId = InputValues.RequiredId(networkId, "NetworkId", PortalVoucherContract.NetworkRequired);
        NetworkScope.Ensure(currentUser, scopedNetworkId);
        var paging = PageRequest.Parse(page, pageSize);

        var network = await networks.FindByIdAsync(scopedNetworkId, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.NetworkNotFound);

        var statusFilter = ParseStatus(status);
        var timeZone = NetworkTime.Resolve(network.TimeZone);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var fromUtc = issuedFrom is null ? (DateTime?)null : NetworkTime.ToUtc(issuedFrom.Value.Date, timeZone, "issuedFrom");
        var toUtcExclusive = issuedTo is null
            ? (DateTime?)null
            : NetworkTime.ToUtc(issuedTo.Value.Date.AddDays(1), timeZone, "issuedTo");

        if (fromUtc is { } start && toUtcExclusive is { } end && start >= end)
        {
            throw new RequestValidationException("issuedFrom", PortalVoucherContract.IssuedRangeInvalid);
        }

        var searchDigits = DigitsOnly(search);
        var searchText = search?.Trim();

        // Filtros e página no banco (spec GH-071, D-5); o status efetivo da resposta é recalculado no domínio.
        var filter = new PortalVoucherFilter(scopedNetworkId, campaignId, statusFilter, fromUtc, toUtcExclusive, searchText, searchDigits, utcNow);
        var (total, items) = await vouchers.ListPageAsync(filter, paging?.Skip, paging?.Take, cancellationToken);

        var responses = items
            .Select(voucher => (Voucher: voucher, Status: voucher.EffectiveStatus(utcNow)))
            .Where(entry => statusFilter is null
                ? entry.Status != VoucherStatus.Discarded
                : entry.Status == statusFilter)
            .Where(entry => MatchesSearch(entry.Voucher, searchText, searchDigits))
            .Select(entry => MapListItem(entry.Voucher, entry.Status, timeZone))
            .ToList();

        return new PagedResult<PortalVoucherListItemResponse>(responses, total);
    }

    public async Task<PortalVoucherDetailResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var voucher = await vouchers.FindByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(PortalVoucherContract.VoucherNotFound);

        NetworkScope.Ensure(currentUser, voucher.NetworkId);

        var campaign = voucher.Campaign ?? throw VoucherMapper.MissingNavigation(nameof(Voucher.Campaign));
        var network = campaign.Network ?? throw VoucherMapper.MissingNavigation(nameof(Campaign.Network));
        var item = voucher.Item ?? throw VoucherMapper.MissingNavigation(nameof(Voucher.Item));
        var timeZone = NetworkTime.Resolve(network.TimeZone);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var status = voucher.EffectiveStatus(utcNow);
        var establishment = await vouchers.FindEstablishmentAsync(voucher.EstablishmentId, cancellationToken);

        var listItem = MapListItem(voucher, status, timeZone);
        var benefit = FormatBenefit(item);
        var scope = BuildScope(establishment, voucher.Channel);
        var events = voucher.Events
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .Select(entry => MapEvent(entry, establishment, voucher.Channel, timeZone))
            .ToList();

        return new PortalVoucherDetailResponse(listItem, benefit, scope, events);
    }

    private static bool MatchesSearch(Voucher voucher, string? searchText, string? searchDigits)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        // CPF só pelo número inteiro: um pedaço, junto com os 6 dígitos que a máscara mostra, permitiria descobrir o CPF
        // por tentativa (D4; retenção e base legal em PA-4 da spec GH-047).
        if (searchDigits is { Length: CpfLength } && voucher.CustomerDocument == searchDigits)
        {
            return true;
        }

        return voucher.Code.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private static string? DigitsOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    private static VoucherStatus? ParseStatus(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (!Enum.TryParse<VoucherStatus>(value.Trim(), ignoreCase: true, out var status)
            || status == VoucherStatus.Discarded)
        {
            throw new RequestValidationException("status", PortalVoucherContract.StatusInvalid);
        }

        return status;
    }

    private static PortalVoucherListItemResponse MapListItem(Voucher voucher, VoucherStatus status, TimeZoneInfo timeZone)
    {
        var campaign = voucher.Campaign ?? throw VoucherMapper.MissingNavigation(nameof(Voucher.Campaign));

        return new PortalVoucherListItemResponse(
            voucher.Id,
            voucher.Code,
            campaign.Id,
            campaign.Name,
            CustomerName: null,
            CustomerDocumentMask.Mask(voucher.CustomerDocument),
            NetworkTime.ToLocal(voucher.IssuedAt, timeZone),
            NetworkTime.ToLocal(voucher.ValidUntil, timeZone),
            voucher.UsesCount,
            voucher.MaxUses,
            status.ToString());
    }

    private static string FormatBenefit(CampaignItem item) =>
        item.BenefitType == BenefitType.Percentage
            ? $"{item.BenefitValue.ToString("0.##", PtBr)}%"
            : item.BenefitValue.ToString("C", PtBr);

    private static IReadOnlyList<PortalVoucherScopeItemResponse> BuildScope(Establishment? establishment, ChannelType channel) =>
    [
        new("Loja", establishment?.Name ?? establishment?.Code ?? "Loja não encontrada"),
        new("Canal", channel == ChannelType.Physical ? "Físico" : "Virtual")
    ];

    private static PortalVoucherEventResponse MapEvent(
        VoucherEvent entry,
        Establishment? establishment,
        ChannelType channel,
        TimeZoneInfo timeZone)
    {
        var place = establishment is null
            ? ChannelLabel(channel)
            : $"{establishment.Name} · {ChannelLabel(channel)}";

        return new PortalVoucherEventResponse(
            entry.Id,
            entry.EventType,
            NetworkTime.ToLocal(entry.OccurredAt, timeZone),
            OriginLabel(entry.ActorType),
            place,
            ResultLabel(entry));
    }

    private static string ChannelLabel(ChannelType channel) =>
        channel == ChannelType.Physical ? "Físico" : "Virtual";

    private static string OriginLabel(Domain.Audit.AuditActorType? actorType) =>
        actorType switch
        {
            Domain.Audit.AuditActorType.User => "Portal",
            Domain.Audit.AuditActorType.Application => "Aplicação satélite",
            _ => "Sistema"
        };

    private static string ResultLabel(VoucherEvent entry) =>
        entry.EventType switch
        {
            VoucherEventTypes.Issued => "Voucher emitido",
            VoucherEventTypes.Confirmed => "Venda confirmada",
            // O cancelamento da campanha (spec GH-061) grava o mesmo tipo com a causa no payload.
            VoucherEventTypes.Cancelled when entry.Payload?.Contains(CampaignVoucherCanceller.Cause, StringComparison.Ordinal) == true
                => "Campanha cancelada",
            VoucherEventTypes.Cancelled => "Concessão desfeita",
            VoucherEventTypes.ReservationExpired => "Reserva expirada",
            VoucherEventTypes.Redeemed => "Resgate confirmado",
            VoucherEventTypes.RedemptionReversed => "Resgate estornado",
            VoucherEventTypes.Expired => "Validade encerrada",
            _ => entry.EventType
        };
}
