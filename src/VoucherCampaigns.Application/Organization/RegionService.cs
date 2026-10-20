using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Organization;

public interface IRegionService
{
    Task<IReadOnlyList<RegionResponse>> GetAllAsync(Guid? networkId, bool? activeOnly, CancellationToken ct = default);
    Task<RegionResponse?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RegionResponse> CreateAsync(CreateRegionRequest request, CancellationToken ct = default);
    Task<RegionResponse?> UpdateAsync(Guid id, UpdateRegionRequest request, CancellationToken ct = default);
    Task<bool> InactivateAsync(Guid id, CancellationToken ct = default);
}

public class RegionService(IOrganizationRepository repository) : IRegionService
{
    public async Task<IReadOnlyList<RegionResponse>> GetAllAsync(Guid? networkId, bool? activeOnly, CancellationToken ct = default)
    {
        var items = await repository.GetRegionsAsync(networkId, activeOnly, ct);
        return items.Select(r => new RegionResponse(r.Id, r.NetworkId, r.Name, r.IsActive, r.CreatedAt)).ToList();
    }

    public async Task<RegionResponse?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var region = await repository.GetRegionByIdAsync(id, ct);
        return region is null ? null : new RegionResponse(region.Id, region.NetworkId, region.Name, region.IsActive, region.CreatedAt);
    }

    public async Task<RegionResponse> CreateAsync(CreateRegionRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("O nome da região é obrigatório.");

        // Validação de integridade: a rede pai deve existir e estar ativa
        var parentNetwork = await repository.GetNetworkByIdAsync(request.NetworkId, ct);
        if (parentNetwork is null || !parentNetwork.IsActive)
            throw new InvalidOperationException("A rede informada não existe ou está inativa.");

        if (await repository.ExistsRegionByNameInNetworkAsync(request.NetworkId, request.Name.Trim(), null, ct))
            throw new InvalidOperationException($"Já existe uma região com o nome '{request.Name}' nesta rede.");

        var region = new Region
        {
            Id = Guid.CreateVersion7(),
            NetworkId = request.NetworkId,
            Name = request.Name.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var created = await repository.CreateRegionAsync(region, ct);
        return new RegionResponse(created.Id, created.NetworkId, created.Name, created.IsActive, created.CreatedAt);
    }

    public async Task<RegionResponse?> UpdateAsync(Guid id, UpdateRegionRequest request, CancellationToken ct = default)
    {
        var existing = await repository.GetRegionByIdAsync(id, ct);
        if (existing is null) return null;

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("O nome da região é obrigatório.");

        if (await repository.ExistsRegionByNameInNetworkAsync(existing.NetworkId, request.Name.Trim(), id, ct))
            throw new InvalidOperationException($"Já existe uma região com o nome '{request.Name}' nesta rede.");

        var wasActive = existing.IsActive;
        existing.Name = request.Name.Trim();
        existing.IsActive = request.IsActive;

        var updated = await repository.UpdateRegionAsync(existing, ct);

        // Inativação em cascata
        if (wasActive && !request.IsActive)
        {
            await repository.InactivateRegionCascadeAsync(id, ct);
        }

        return new RegionResponse(updated.Id, updated.NetworkId, updated.Name, updated.IsActive, updated.CreatedAt);
    }

    public async Task<bool> InactivateAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await repository.GetRegionByIdAsync(id, ct);
        if (existing is null) return false;

        existing.IsActive = false;
        await repository.UpdateRegionAsync(existing, ct);
        await repository.InactivateRegionCascadeAsync(id, ct);
        return true;
    }
}
