using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Application.Reports;

public sealed record ReportVoucherRow(
    Guid VoucherId,
    Guid CampaignId,
    string CampaignName,
    Guid EstablishmentId,
    Guid RegionId,
    string RegionName,
    DateTime IssuedAt,
    VoucherStatus Status,
    int UsesCount,
    DateTime ValidUntil,
    DateTime? ReservedUntil);

public interface IReportReadRepository
{
    Task<IReadOnlyList<ReportVoucherRow>> ListVouchersAsync(Guid networkId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReportOptionResponse>> ListCampaignsAsync(Guid networkId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReportOptionResponse>> ListRegionsAsync(Guid networkId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReportEstablishmentOptionResponse>> ListEstablishmentsAsync(
        Guid networkId, CancellationToken cancellationToken);
}
