using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Application.Vouchers;

public interface IPortalVoucherReadRepository
{
    Task<IReadOnlyList<Voucher>> ListByNetworkAsync(Guid networkId, Guid? campaignId, CancellationToken cancellationToken);

    Task<Voucher?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Establishment?> FindEstablishmentAsync(Guid establishmentId, CancellationToken cancellationToken);
}
