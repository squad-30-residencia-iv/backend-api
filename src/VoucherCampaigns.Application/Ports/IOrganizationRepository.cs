using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Ports;

public interface IOrganizationRepository
{
    Task<Network?> GetNetworkByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Network> CreateNetworkAsync(Network network, CancellationToken cancellationToken);

    Task<Network> UpdateNetworkAsync(Network network, CancellationToken cancellationToken);

    Task<bool> ExistsNetworkByNameAsync(string name, Guid? excludeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Region>> GetRegionsAsync(Guid networkId, bool? activeOnly, CancellationToken cancellationToken);

    Task<Region?> GetRegionByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Region> CreateRegionAsync(Region region, CancellationToken cancellationToken);

    Task<Region> UpdateRegionAsync(Region region, CancellationToken cancellationToken);

    Task<bool> ExistsRegionByNameInNetworkAsync(Guid networkId, string name, Guid? excludeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Establishment>> GetEstablishmentsAsync(Guid regionId, bool? activeOnly, CancellationToken cancellationToken);

    Task<Establishment?> GetEstablishmentByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Establishment> CreateEstablishmentAsync(Establishment establishment, CancellationToken cancellationToken);

    Task<Establishment> UpdateEstablishmentAsync(Establishment establishment, CancellationToken cancellationToken);

    Task<bool> ExistsEstablishmentByCodeInRegionAsync(Guid regionId, string code, Guid? excludeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Channel>> GetChannelsAsync(Guid establishmentId, bool? activeOnly, CancellationToken cancellationToken);

    Task<Channel?> GetChannelByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Channel> CreateChannelAsync(Channel channel, CancellationToken cancellationToken);

    Task<Channel> UpdateChannelAsync(Channel channel, CancellationToken cancellationToken);

    Task<bool> ExistsChannelByTypeInEstablishmentAsync(
        Guid establishmentId, ChannelType type, Guid? excludeId, CancellationToken cancellationToken);
}
