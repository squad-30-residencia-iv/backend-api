namespace VoucherCampaigns.Application.Vouchers;

public interface IPortalVoucherQueryService
{
    Task<IReadOnlyList<PortalVoucherListItemResponse>> ListAsync(
        Guid? networkId,
        Guid? campaignId,
        string? status,
        string? search,
        DateTime? issuedFrom,
        DateTime? issuedTo,
        CancellationToken cancellationToken);

    Task<PortalVoucherDetailResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
