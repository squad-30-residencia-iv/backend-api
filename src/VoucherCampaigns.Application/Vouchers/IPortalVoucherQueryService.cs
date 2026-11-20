using VoucherCampaigns.Application.Common;

namespace VoucherCampaigns.Application.Vouchers;

public interface IPortalVoucherQueryService
{
    /// <summary>Listagem do portal com a página opcional (spec GH-071, D-5): sem <c>page</c> e <c>pageSize</c>, traz tudo.</summary>
    Task<PagedResult<PortalVoucherListItemResponse>> ListAsync(
        Guid? networkId,
        Guid? campaignId,
        string? status,
        string? search,
        DateTime? issuedFrom,
        DateTime? issuedTo,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken);

    Task<PortalVoucherDetailResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
