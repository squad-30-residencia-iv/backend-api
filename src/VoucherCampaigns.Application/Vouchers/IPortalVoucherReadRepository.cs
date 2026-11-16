using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Application.Vouchers;

public interface IPortalVoucherReadRepository
{
    Task<IReadOnlyList<Voucher>> ListByNetworkAsync(Guid networkId, Guid? campaignId, CancellationToken cancellationToken);

    Task<Voucher?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Establishment?> FindEstablishmentAsync(Guid establishmentId, CancellationToken cancellationToken);

    /// <summary>Contagens por campanha na rede (todas as linhas de voucher, status gravado; o serviço aplica o efetivo).</summary>
    Task<IReadOnlyList<Voucher>> ListForCampaignsAsync(Guid networkId, IReadOnlyCollection<Guid> campaignIds, CancellationToken cancellationToken);
}
