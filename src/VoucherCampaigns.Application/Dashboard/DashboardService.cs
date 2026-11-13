using System.Globalization;
using System.Text;
using System.Text.Json;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Dashboard;

/// <summary>Acompanhamento da campanha (EP-03, spec GH-050): indicadores, filtros, trilha e CSV. Só leitura.</summary>
public class DashboardService(
    IDashboardRepository dashboard,
    ICampaignRepository campaigns,
    INetworkRepository networks,
    IOrganizationRepository organization,
    ICatalogRepository catalog,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IDashboardService
{
    private static readonly CultureInfo Brazil = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<CampaignDashboardResponse> GetCampaignAsync(Guid campaignId, DashboardFilters filters, CancellationToken cancellationToken)
    {
        var campaign = await RequireCampaignAsync(campaignId, cancellationToken);
        var network = campaign.Network!;
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var resolved = await ResolveFiltersAsync(filters, network, cancellationToken);

        var counts = await dashboard.CountVouchersAsync(network.Id, campaign.Id, resolved.Filter, utcNow, cancellationToken);
        var timeZone = NetworkTime.Resolve(network.TimeZone);

        return new CampaignDashboardResponse(
            campaign.Id,
            campaign.Name,
            campaign.EffectiveStatus(utcNow).ToString(),
            NetworkTime.ToLocal(campaign.StartsAt, timeZone),
            NetworkTime.ToLocal(campaign.EndsAt, timeZone),
            resolved.Response,
            Indicators(counts.SingleOrDefault()),
            NetworkTime.ToLocal(utcNow, timeZone));
    }

    public async Task<NetworkDashboardResponse> GetNetworkAsync(Guid? networkId, string? status, DashboardFilters filters, CancellationToken cancellationToken)
    {
        var scopedNetworkId = InputValues.RequiredId(networkId, "NetworkId", CampaignContract.NetworkRequired);
        NetworkScope.Ensure(currentUser, scopedNetworkId);
        var statusFilter = status is null ? (CampaignStatus?)null : ParseEnum<CampaignStatus>(status, "Status", CampaignContract.StatusInvalid);

        var network = await networks.FindByIdAsync(scopedNetworkId, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.NetworkNotFound);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var resolved = await ResolveFiltersAsync(filters, network, cancellationToken);
        var timeZone = NetworkTime.Resolve(network.TimeZone);

        var counts = (await dashboard.CountVouchersAsync(network.Id, null, resolved.Filter, utcNow, cancellationToken))
            .ToDictionary(entry => entry.CampaignId);

        var summaries = (await campaigns.GetByNetworkAsync(network.Id, cancellationToken))
            .Where(campaign => statusFilter is null || campaign.EffectiveStatus(utcNow) == statusFilter)
            .Where(campaign => resolved.Audience is null || campaign.AudienceType == resolved.Audience)
            .OrderByDescending(campaign => campaign.EndsAt)
            .ThenBy(campaign => campaign.Name)
            .ThenBy(campaign => campaign.Id)
            .Select(campaign => new CampaignSummaryResponse(
                campaign.Id,
                campaign.Name,
                campaign.EffectiveStatus(utcNow).ToString(),
                NetworkTime.ToLocal(campaign.StartsAt, timeZone),
                NetworkTime.ToLocal(campaign.EndsAt, timeZone),
                campaign.AudienceType?.ToString(),
                Indicators(counts.GetValueOrDefault(campaign.Id))))
            .ToList();

        return new NetworkDashboardResponse(network.Id, resolved.Response, summaries, Totals(summaries), NetworkTime.ToLocal(utcNow, timeZone));
    }

    public async Task<CampaignAuditResponse> GetAuditAsync(Guid campaignId, CancellationToken cancellationToken)
    {
        var campaign = await RequireCampaignAsync(campaignId, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var timeZone = NetworkTime.Resolve(campaign.Network!.TimeZone);

        var entries = await dashboard.GetCampaignAuditAsync(campaign.Id, cancellationToken);
        var userIds = entries.Where(entry => entry.ActorType == AuditActorType.User).Select(entry => entry.ActorId).Distinct().ToList();
        var users = await dashboard.GetUsersAsync(userIds, cancellationToken);

        var mapped = entries
            .Select(entry => new CampaignAuditEntryResponse(
                entry.Id,
                entry.EventType,
                NetworkTime.ToLocal(entry.OccurredAt, timeZone),
                entry.ActorType == AuditActorType.User
                    ? new AuditActorResponse("User", entry.ActorId, users.GetValueOrDefault(entry.ActorId)?.Name, users.GetValueOrDefault(entry.ActorId)?.Role)
                    : new AuditActorResponse("Application", entry.ActorId, null, null),
                Synthetic: false,
                ParsePayload(entry.Payload)))
            .ToList();

        // Campanha anterior à auditoria da criação: a lista nunca vem sem origem (US-03.4, cenário 4; D-9).
        if (mapped.All(entry => entry.EventType != AuditEventTypes.CampaignCreated))
        {
            mapped.Insert(0, new CampaignAuditEntryResponse(
                campaign.Id,
                AuditEventTypes.CampaignCreated,
                NetworkTime.ToLocal(DateTime.SpecifyKind(campaign.CreatedAt, DateTimeKind.Utc), timeZone),
                null,
                Synthetic: true,
                null));
        }

        return new CampaignAuditResponse(campaign.Id, campaign.Name, campaign.EffectiveStatus(utcNow).ToString(), mapped);
    }

    public async Task<CsvFile> ExportNetworkAsync(Guid? networkId, string? status, DashboardFilters filters, CancellationToken cancellationToken)
    {
        var view = await GetNetworkAsync(networkId, status, filters, cancellationToken);

        if (view.Campaigns.Count == 0)
        {
            throw new BusinessRuleException(DashboardContract.ExportEmptyCode, DashboardContract.ExportEmptyMessage);
        }

        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "Gerado em", view.GeneratedAt.ToString("dd/MM/yyyy HH:mm", Brazil) },
            new[] { "Filtros", DescribeFilters(view.Filters, status) },
            Array.Empty<string>(),
            new[] { "Campanha", "Status", "Início", "Fim", "Tipo de cliente", "Emitidos", "Reservados", "Resgatados", "Expirados sem uso", "Cancelados sem uso", "Disponíveis", "Taxa de utilização", "Utilizações", "Desconto total" }
        };

        rows.AddRange(view.Campaigns.Select(campaign => IndicatorRow(
            campaign.Name, campaign.Status, campaign.StartsAt.ToString("dd/MM/yyyy HH:mm", Brazil), campaign.EndsAt.ToString("dd/MM/yyyy HH:mm", Brazil),
            campaign.Audience ?? string.Empty, campaign.Indicators)));
        rows.Add(IndicatorRow("Total", string.Empty, string.Empty, string.Empty, string.Empty, view.Totals));

        return new CsvFile($"acompanhamento-{view.GeneratedAt:yyyyMMdd-HHmm}.csv", Csv.Build(rows));
    }

    public async Task<CsvFile> ExportAuditAsync(Guid campaignId, CancellationToken cancellationToken)
    {
        var trail = await GetAuditAsync(campaignId, cancellationToken);
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "Campanha", trail.Name },
            new[] { "Status", trail.Status },
            Array.Empty<string>(),
            new[] { "Data/hora", "Evento", "Responsável", "Papel", "Origem", "Detalhes" }
        };

        rows.AddRange(trail.Entries.Select(entry => (IReadOnlyList<string>)new[]
        {
            entry.OccurredAt.ToString("dd/MM/yyyy HH:mm:ss", Brazil),
            entry.EventType,
            entry.Actor?.Name ?? (entry.Actor is null ? string.Empty : entry.Actor.Id.ToString()),
            entry.Actor?.Role ?? string.Empty,
            entry.Synthetic ? "deduzido" : entry.Actor?.Type ?? string.Empty,
            entry.Payload?.GetRawText() ?? string.Empty
        }));

        return new CsvFile($"auditoria-campanha-{trail.CampaignId:N}.csv", Csv.Build(rows));
    }

    private async Task<Campaign> RequireCampaignAsync(Guid campaignId, CancellationToken cancellationToken)
    {
        var campaign = await campaigns.FindByIdAsync(campaignId, cancellationToken)
            ?? throw new ResourceNotFoundException(CampaignContract.CampaignNotFound);
        NetworkScope.Ensure(currentUser, campaign.NetworkId);

        return campaign;
    }

    /// <summary>Confere e resolve os filtros (1.3): período no fuso da rede, loja e região da rede, categoria existente, tipo de cliente.</summary>
    private async Task<ResolvedFilters> ResolveFiltersAsync(DashboardFilters filters, Network network, CancellationToken cancellationToken)
    {
        if (filters.From is { } from && filters.To is { } to && from > to)
        {
            throw new RequestValidationException("from", DashboardContract.PeriodInverted);
        }

        var audience = filters.Audience is null
            ? (CampaignAudienceType?)null
            : ParseEnum<CampaignAudienceType>(filters.Audience, "audience", DashboardContract.AudienceInvalid);

        var timeZone = NetworkTime.Resolve(network.TimeZone);
        var fromUtc = filters.From is { } fromDay ? NetworkTime.ToUtc(fromDay.ToDateTime(TimeOnly.MinValue), timeZone, "from") : (DateTime?)null;
        var toUtc = filters.To is { } toDay ? NetworkTime.ToUtc(toDay.AddDays(1).ToDateTime(TimeOnly.MinValue), timeZone, "to") : (DateTime?)null;

        string? establishmentName = null;
        string? regionName = null;
        string? categoryName = null;

        if (filters.EstablishmentId is { } establishmentId)
        {
            var establishment = await organization.GetEstablishmentByIdAsync(establishmentId, cancellationToken)
                ?? throw new ResourceNotFoundException(DashboardContract.EstablishmentNotFound);
            var region = await organization.GetRegionByIdAsync(establishment.RegionId, cancellationToken);

            if (region?.NetworkId != network.Id)
            {
                throw new RequestValidationException("establishmentId", DashboardContract.EstablishmentOutsideNetwork);
            }

            establishmentName = establishment.Name;
        }

        if (filters.RegionId is { } regionId)
        {
            var region = await organization.GetRegionByIdAsync(regionId, cancellationToken)
                ?? throw new ResourceNotFoundException(DashboardContract.RegionNotFound);

            if (region.NetworkId != network.Id)
            {
                throw new RequestValidationException("regionId", DashboardContract.RegionOutsideNetwork);
            }

            regionName = region.Name;
        }

        if (filters.CategoryId is { } categoryId)
        {
            var category = await catalog.GetCategoryByIdAsync(categoryId, cancellationToken)
                ?? throw new ResourceNotFoundException(DashboardContract.CategoryNotFound);
            categoryName = category.Name;
        }

        return new ResolvedFilters(
            new VoucherFilter(fromUtc, toUtc, filters.EstablishmentId, filters.RegionId, filters.CategoryId),
            audience,
            new DashboardFiltersResponse(
                filters.From, filters.To, filters.EstablishmentId, establishmentName, filters.RegionId, regionName,
                filters.CategoryId, categoryName, audience?.ToString()));
    }

    private static CampaignIndicatorsResponse Indicators(CampaignCounts? counts) =>
        counts is null
            ? CampaignIndicatorsResponse.Zero
            : new CampaignIndicatorsResponse(
                counts.Issued, counts.Reserved, counts.Redeemed, counts.ExpiredUnused, counts.CancelledUnused, counts.Available,
                Rate(counts.Redeemed, counts.Issued), counts.Redemptions, counts.DiscountTotal);

    private static CampaignIndicatorsResponse Totals(IReadOnlyList<CampaignSummaryResponse> campaigns)
    {
        var indicators = campaigns.Select(campaign => campaign.Indicators).ToList();
        var issued = indicators.Sum(entry => entry.Issued);
        var redeemed = indicators.Sum(entry => entry.Redeemed);

        return new CampaignIndicatorsResponse(
            issued,
            indicators.Sum(entry => entry.Reserved),
            redeemed,
            indicators.Sum(entry => entry.ExpiredUnused),
            indicators.Sum(entry => entry.CancelledUnused),
            indicators.Sum(entry => entry.Available),
            Rate(redeemed, issued),
            indicators.Sum(entry => entry.Redemptions),
            indicators.Sum(entry => entry.DiscountTotal));
    }

    private static decimal? Rate(int redeemed, int issued) =>
        issued == 0 ? null : Math.Round((decimal)redeemed / issued, 4, MidpointRounding.AwayFromZero);

    private static JsonElement? ParsePayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        using var document = JsonDocument.Parse(payload);

        return document.RootElement.Clone();
    }

    private static string DescribeFilters(DashboardFiltersResponse filters, string? status)
    {
        var parts = new List<string>();

        if (filters.From is not null || filters.To is not null)
        {
            parts.Add($"período {filters.From?.ToString("dd/MM/yyyy", Brazil) ?? "..."} a {filters.To?.ToString("dd/MM/yyyy", Brazil) ?? "..."}");
        }

        if (filters.EstablishmentId is not null)
        {
            parts.Add($"loja {filters.EstablishmentName}");
        }

        if (filters.RegionId is not null)
        {
            parts.Add($"região {filters.RegionName}");
        }

        if (filters.CategoryId is not null)
        {
            parts.Add($"categoria {filters.CategoryName}");
        }

        if (filters.Audience is not null)
        {
            parts.Add($"tipo de cliente {filters.Audience}");
        }

        if (status is not null)
        {
            parts.Add($"status {status}");
        }

        return parts.Count == 0 ? "nenhum" : string.Join("; ", parts);
    }

    private static IReadOnlyList<string> IndicatorRow(string name, string status, string startsAt, string endsAt, string audience, CampaignIndicatorsResponse indicators) =>
    [
        name,
        status,
        startsAt,
        endsAt,
        audience,
        indicators.Issued.ToString(Brazil),
        indicators.Reserved.ToString(Brazil),
        indicators.Redeemed.ToString(Brazil),
        indicators.ExpiredUnused.ToString(Brazil),
        indicators.CancelledUnused.ToString(Brazil),
        indicators.Available.ToString(Brazil),
        indicators.UtilizationRate is { } rate ? (rate * 100).ToString("F2", Brazil) + "%" : "n/a",
        indicators.Redemptions.ToString(Brazil),
        indicators.DiscountTotal.ToString("F2", Brazil)
    ];

    private static TEnum ParseEnum<TEnum>(string value, string field, string message) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new RequestValidationException(field, message);

    private sealed record ResolvedFilters(VoucherFilter Filter, CampaignAudienceType? Audience, DashboardFiltersResponse Response);
}

/// <summary>CSV para o Excel em português (spec GH-050, D-10): UTF-8 com BOM, <c>;</c>, <c>CRLF</c>, aspas quando preciso.</summary>
public static class Csv
{
    public static byte[] Build(IEnumerable<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();

        foreach (var row in rows)
        {
            builder.Append(string.Join(';', row.Select(Escape))).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(builder.ToString())];
    }

    private static string Escape(string value) =>
        value.IndexOfAny([';', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
