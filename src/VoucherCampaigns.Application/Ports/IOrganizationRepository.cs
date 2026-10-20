using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Ports;

public interface IOrganizationRepository
{
    // Networks
    Task<IReadOnlyList<Network>> GetAllNetworksAsync(bool? activeOnly, CancellationToken ct);
    Task<Network?> GetNetworkByIdAsync(Guid id, CancellationToken ct);
    Task<Network> CreateNetworkAsync(Network network, CancellationToken ct);
    Task<Network> UpdateNetworkAsync(Network network, CancellationToken ct);
    Task<bool> ExistsNetworkByNameAsync(string name, Guid? excludeId, CancellationToken ct);
    Task InactivateNetworkCascadeAsync(Guid networkId, CancellationToken ct);

    // Regions
    Task<IReadOnlyList<Region>> GetRegionsAsync(Guid? networkId, bool? activeOnly, CancellationToken ct);
    Task<Region?> GetRegionByIdAsync(Guid id, CancellationToken ct);
    Task<Region> CreateRegionAsync(Region region, CancellationToken ct);
    Task<Region> UpdateRegionAsync(Region region, CancellationToken ct);
    Task<bool> ExistsRegionByNameInNetworkAsync(Guid networkId, string name, Guid? excludeId, CancellationToken ct);
    Task InactivateRegionCascadeAsync(Guid regionId, CancellationToken ct);

    // Establishments
    Task<IReadOnlyList<Establishment>> GetEstablishmentsAsync(Guid? regionId, bool? activeOnly, CancellationToken ct);
    Task<Establishment?> GetEstablishmentByIdAsync(Guid id, CancellationToken ct);
    Task<Establishment> CreateEstablishmentAsync(Establishment establishment, CancellationToken ct);
    Task<Establishment> UpdateEstablishmentAsync(Establishment establishment, CancellationToken ct);
    Task<bool> ExistsEstablishmentByCodeInRegionAsync(Guid regionId, string code, Guid? excludeId, CancellationToken ct);
    Task InactivateEstablishmentCascadeAsync(Guid establishmentId, CancellationToken ct);

    // Channels
    Task<IReadOnlyList<Channel>> GetChannelsAsync(Guid? establishmentId, bool? activeOnly, CancellationToken ct);
    Task<Channel?> GetChannelByIdAsync(Guid id, CancellationToken ct);
    Task<Channel> CreateChannelAsync(Channel channel, CancellationToken ct);
    Task<Channel> UpdateChannelAsync(Channel channel, CancellationToken ct);
    Task<bool> ExistsChannelByTypeInEstablishmentAsync(Guid establishmentId, ChannelType type, Guid? excludeId, CancellationToken ct);
}
