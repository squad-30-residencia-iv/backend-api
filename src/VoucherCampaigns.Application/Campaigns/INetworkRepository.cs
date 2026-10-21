using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Campaigns;

public interface INetworkRepository
{
    Task<Network?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
}
