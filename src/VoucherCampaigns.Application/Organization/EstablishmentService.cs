using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Organization;

public interface IEstablishmentService
{
    Task<IReadOnlyList<EstablishmentResponse>> GetAllAsync(Guid? regionId, bool? activeOnly, CancellationToken ct = default);
    Task<EstablishmentResponse?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<EstablishmentResponse> CreateAsync(CreateEstablishmentRequest request, CancellationToken ct = default);
    Task<EstablishmentResponse?> UpdateAsync(Guid id, UpdateEstablishmentRequest request, CancellationToken ct = default);
    Task<bool> InactivateAsync(Guid id, CancellationToken ct = default);
}

public class EstablishmentService(IOrganizationRepository repository) : IEstablishmentService
{
    public async Task<IReadOnlyList<EstablishmentResponse>> GetAllAsync(Guid? regionId, bool? activeOnly, CancellationToken ct = default)
    {
        var items = await repository.GetEstablishmentsAsync(regionId, activeOnly, ct);
        return items.Select(e => new EstablishmentResponse(e.Id, e.RegionId, e.Code, e.Name, e.IsActive, e.CreatedAt)).ToList();
    }

    public async Task<EstablishmentResponse?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var establishment = await repository.GetEstablishmentByIdAsync(id, ct);
        return establishment is null
            ? null
            : new EstablishmentResponse(establishment.Id, establishment.RegionId, establishment.Code, establishment.Name, establishment.IsActive, establishment.CreatedAt);
    }

    public async Task<EstablishmentResponse> CreateAsync(CreateEstablishmentRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new ArgumentException("O código do estabelecimento é obrigatório.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("O nome do estabelecimento é obrigatório.");

        // Validação de integridade: a região pai deve existir e estar ativa
        var parentRegion = await repository.GetRegionByIdAsync(request.RegionId, ct);
        if (parentRegion is null || !parentRegion.IsActive)
            throw new InvalidOperationException("A região informada não existe ou está inativa.");

        if (await repository.ExistsEstablishmentByCodeInRegionAsync(request.RegionId, request.Code.Trim(), null, ct))
            throw new InvalidOperationException($"Já existe um estabelecimento com o código '{request.Code}' nesta região.");

        var establishment = new Establishment
        {
            Id = Guid.CreateVersion7(),
            RegionId = request.RegionId,
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var created = await repository.CreateEstablishmentAsync(establishment, ct);
        return new EstablishmentResponse(created.Id, created.RegionId, created.Code, created.Name, created.IsActive, created.CreatedAt);
    }

    public async Task<EstablishmentResponse?> UpdateAsync(Guid id, UpdateEstablishmentRequest request, CancellationToken ct = default)
    {
        var existing = await repository.GetEstablishmentByIdAsync(id, ct);
        if (existing is null) return null;

        if (string.IsNullOrWhiteSpace(request.Code))
            throw new ArgumentException("O código do estabelecimento é obrigatório.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("O nome do estabelecimento é obrigatório.");

        if (await repository.ExistsEstablishmentByCodeInRegionAsync(existing.RegionId, request.Code.Trim(), id, ct))
            throw new InvalidOperationException($"Já existe um estabelecimento com o código '{request.Code}' nesta região.");

        var wasActive = existing.IsActive;
        existing.Code = request.Code.Trim();
        existing.Name = request.Name.Trim();
        existing.IsActive = request.IsActive;

        var updated = await repository.UpdateEstablishmentAsync(existing, ct);

        // Inativação em cascata para os canais
        if (wasActive && !request.IsActive)
        {
            await repository.InactivateEstablishmentCascadeAsync(id, ct);
        }

        return new EstablishmentResponse(updated.Id, updated.RegionId, updated.Code, updated.Name, updated.IsActive, updated.CreatedAt);
    }

    public async Task<bool> InactivateAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await repository.GetEstablishmentByIdAsync(id, ct);
        if (existing is null) return false;

        existing.IsActive = false;
        await repository.UpdateEstablishmentAsync(existing, ct);
        await repository.InactivateEstablishmentCascadeAsync(id, ct);
        return true;
    }
}
