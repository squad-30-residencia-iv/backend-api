using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Ports;

public interface IOrganizationRepository
{
    Task<IReadOnlyList<Network>> GetNetworksAsync(bool? activeOnly, CancellationToken cancellationToken);

    Task<Network?> GetNetworkByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Network> CreateNetworkAsync(Network network, CancellationToken cancellationToken);

    Task<Network> UpdateNetworkAsync(Network network, CancellationToken cancellationToken);

    Task<bool> ExistsNetworkByNameAsync(string name, Guid? excludeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Region>> GetRegionsAsync(Guid networkId, bool? activeOnly, CancellationToken cancellationToken);

    Task<Region?> GetRegionByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Region> CreateRegionAsync(Region region, CancellationToken cancellationToken);

    Task<Region> UpdateRegionAsync(Region region, CancellationToken cancellationToken);

    Task<bool> ExistsRegionByNameInNetworkAsync(Guid networkId, string name, Guid? excludeId, CancellationToken cancellationToken);

    /// <summary>Lojas da região, com os canais carregados.</summary>
    Task<IReadOnlyList<Establishment>> GetEstablishmentsAsync(Guid regionId, bool? activeOnly, CancellationToken cancellationToken);

    /// <summary>Loja com os canais carregados.</summary>
    Task<Establishment?> GetEstablishmentByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Grava a loja e os canais dela.</summary>
    Task<Establishment> CreateEstablishmentAsync(Establishment establishment, CancellationToken cancellationToken);

    /// <summary>Grava código, nome, situação e canais da loja na mesma operação.</summary>
    Task<Establishment> UpdateEstablishmentAsync(Establishment establishment, CancellationToken cancellationToken);

    Task<bool> ExistsEstablishmentByCodeInRegionAsync(Guid regionId, string code, Guid? excludeId, CancellationToken cancellationToken);
}
