using System.Text.Json;
using VoucherCampaigns.Domain.Audit;

namespace VoucherCampaigns.Application.Dashboard;

/// <summary>Mensagens de contrato do acompanhamento (spec GH-050, seção 2).</summary>
public static class DashboardContract
{
    public const string PeriodInverted = "A data inicial não pode ser depois da data final.";
    public const string AudienceInvalid = "Tipo de cliente inválido. Use AllCustomers ou Restricted.";
    public const string EstablishmentOutsideNetwork = "A loja informada não pertence à rede.";
    public const string RegionOutsideNetwork = "A região informada não pertence à rede.";
    public const string EstablishmentNotFound = "Loja não encontrada.";
    public const string RegionNotFound = "Região não encontrada.";
    public const string CategoryNotFound = "Categoria não encontrada.";

    public const string ExportEmptyCode = "EXPORT_EMPTY";
    public const string ExportEmptyMessage = "Não há dados para exportar.";
}

/// <summary>Filtros da visão (US-03.2), todos opcionais e cumulativos; datas no fuso da rede, inclusivas.</summary>
public record DashboardFilters(DateOnly? From, DateOnly? To, Guid? EstablishmentId, Guid? RegionId, Guid? CategoryId, string? Audience)
{
    public static readonly DashboardFilters None = new(null, null, null, null, null, null);
}

/// <summary>Indicadores de uma campanha (US-03.1, spec GH-050 1.2): <c>issued = redeemed + expiredUnused + cancelledUnused + available</c>.</summary>
/// <param name="UtilizationRate"><c>redeemed / issued</c> com 4 casas; nulo sem emissão.</param>
public record CampaignIndicatorsResponse(
    int Issued,
    int Reserved,
    int Redeemed,
    int ExpiredUnused,
    int CancelledUnused,
    int Available,
    decimal? UtilizationRate,
    int Redemptions,
    decimal DiscountTotal)
{
    public static readonly CampaignIndicatorsResponse Zero = new(0, 0, 0, 0, 0, 0, null, 0, 0m);
}

public record DashboardFiltersResponse(
    DateOnly? From,
    DateOnly? To,
    Guid? EstablishmentId,
    string? EstablishmentName,
    Guid? RegionId,
    string? RegionName,
    Guid? CategoryId,
    string? CategoryName,
    string? Audience);

public record CampaignDashboardResponse(
    Guid CampaignId,
    string Name,
    string Status,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DashboardFiltersResponse Filters,
    CampaignIndicatorsResponse Indicators,
    DateTimeOffset GeneratedAt);

public record CampaignSummaryResponse(
    Guid CampaignId,
    string Name,
    string Status,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Audience,
    CampaignIndicatorsResponse Indicators);

public record NetworkDashboardResponse(
    Guid NetworkId,
    DashboardFiltersResponse Filters,
    IReadOnlyList<CampaignSummaryResponse> Campaigns,
    CampaignIndicatorsResponse Totals,
    DateTimeOffset GeneratedAt);

/// <param name="Synthetic">Evento de criação deduzido de <c>created_at</c> para campanhas anteriores à auditoria da criação (D-9).</param>
public record CampaignAuditEntryResponse(
    Guid Id,
    string EventType,
    DateTimeOffset OccurredAt,
    AuditActorResponse? Actor,
    bool Synthetic,
    JsonElement? Payload);

public record AuditActorResponse(string Type, Guid Id, string? Name, string? Role);

public record CampaignAuditResponse(Guid CampaignId, string Name, string Status, IReadOnlyList<CampaignAuditEntryResponse> Entries);

/// <summary>Arquivo CSV em UTF-8 com BOM (D-10).</summary>
public record CsvFile(string FileName, byte[] Content)
{
    public const string ContentType = "text/csv; charset=utf-8";
}

public interface IDashboardService
{
    Task<CampaignDashboardResponse> GetCampaignAsync(Guid campaignId, DashboardFilters filters, CancellationToken cancellationToken);

    Task<NetworkDashboardResponse> GetNetworkAsync(Guid? networkId, string? status, DashboardFilters filters, CancellationToken cancellationToken);

    Task<CampaignAuditResponse> GetAuditAsync(Guid campaignId, CancellationToken cancellationToken);

    Task<CsvFile> ExportNetworkAsync(Guid? networkId, string? status, DashboardFilters filters, CancellationToken cancellationToken);

    Task<CsvFile> ExportAuditAsync(Guid campaignId, CancellationToken cancellationToken);
}

/// <summary>Contagens de uma campanha pelo status efetivo calculado no SQL (spec GH-050, D-3 e D-4).</summary>
public record CampaignCounts(
    Guid CampaignId,
    int Issued,
    int Reserved,
    int Redeemed,
    int ExpiredUnused,
    int CancelledUnused,
    int Available,
    int Redemptions,
    decimal DiscountTotal);

/// <summary>Filtros já resolvidos para a consulta: período em UTC e ids conferidos.</summary>
public record VoucherFilter(DateTime? FromUtc, DateTime? ToUtcExclusive, Guid? EstablishmentId, Guid? RegionId, Guid? CategoryId);

public record AuditActor(Guid Id, string Name, string Role);

public interface IDashboardRepository
{
    /// <summary>Uma linha por campanha da rede com vouchers no recorte; campanha sem voucher não aparece.</summary>
    Task<IReadOnlyList<CampaignCounts>> CountVouchersAsync(Guid networkId, Guid? campaignId, VoucherFilter filter, DateTime utcNow, CancellationToken cancellationToken);

    /// <summary>Trilha da campanha (<c>entity_type = Campaign</c>), em ordem cronológica crescente.</summary>
    Task<IReadOnlyList<AuditEntry>> GetCampaignAuditAsync(Guid campaignId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, AuditActor>> GetUsersAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}
