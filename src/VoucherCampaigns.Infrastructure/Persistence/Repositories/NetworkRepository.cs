using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Infrastructure.Persistence.Repositories;

public class NetworkRepository(AppDbContext context) : INetworkRepository
{
    public Task<Network?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Networks.AsNoTracking().FirstOrDefaultAsync(network => network.Id == id, cancellationToken);
}
