using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Organization;

public interface INetworkService
{
    Task<IReadOnlyList<NetworkResponse>> GetAllAsync(bool? activeOnly, CancellationToken ct = default);
    Task<NetworkResponse?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<NetworkResponse> CreateAsync(CreateNetworkRequest request, CancellationToken ct = default);
    Task<NetworkResponse?> UpdateAsync(Guid id, UpdateNetworkRequest request, CancellationToken ct = default);
    Task<bool> InactivateAsync(Guid id, CancellationToken ct = default);
}

public class NetworkService(IOrganizationRepository repository) : INetworkService
{
    public async Task<IReadOnlyList<NetworkResponse>> GetAllAsync(bool? activeOnly, CancellationToken ct = default)
    {
        var items = await repository.GetAllNetworksAsync(activeOnly, ct);
        return items.Select(n => new NetworkResponse(n.Id, n.Name, n.IsActive, n.CreatedAt)).ToList();
    }

    public async Task<NetworkResponse?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var network = await repository.GetNetworkByIdAsync(id, ct);
        return network is null ? null : new NetworkResponse(network.Id, network.Name, network.IsActive, network.CreatedAt);
    }

    public async Task<NetworkResponse> CreateAsync(CreateNetworkRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("O nome da rede é obrigatório.");

        if (await repository.ExistsNetworkByNameAsync(request.Name.Trim(), null, ct))
            throw new InvalidOperationException($"Já existe uma rede cadastrada com o nome '{request.Name}'.");

        var network = new Network
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var created = await repository.CreateNetworkAsync(network, ct);
        return new NetworkResponse(created.Id, created.Name, created.IsActive, created.CreatedAt);
    }

    public async Task<NetworkResponse?> UpdateAsync(Guid id, UpdateNetworkRequest request, CancellationToken ct = default)
    {
        var existing = await repository.GetNetworkByIdAsync(id, ct);
        if (existing is null) return null;

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("O nome da rede é obrigatório.");

        if (await repository.ExistsNetworkByNameAsync(request.Name.Trim(), id, ct))
            throw new InvalidOperationException($"Já existe uma rede com o nome '{request.Name}'.");

        var wasActive = existing.IsActive;
        existing.Name = request.Name.Trim();
        existing.IsActive = request.IsActive;

        var updated = await repository.UpdateNetworkAsync(existing, ct);

        // Inativação em cascata se foi inativado
        if (wasActive && !request.IsActive)
        {
            await repository.InactivateNetworkCascadeAsync(id, ct);
        }

        return new NetworkResponse(updated.Id, updated.Name, updated.IsActive, updated.CreatedAt);
    }

    public async Task<bool> InactivateAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await repository.GetNetworkByIdAsync(id, ct);
        if (existing is null) return false;

        existing.IsActive = false;
        await repository.UpdateNetworkAsync(existing, ct);
        await repository.InactivateNetworkCascadeAsync(id, ct);
        return true;
    }
}
