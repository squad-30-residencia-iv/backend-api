using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Organization;

public interface IChannelService
{
    Task<IReadOnlyList<ChannelResponse>> GetAllAsync(Guid? establishmentId, bool? activeOnly, CancellationToken ct = default);
    Task<ChannelResponse?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ChannelResponse> CreateAsync(CreateChannelRequest request, CancellationToken ct = default);
    Task<ChannelResponse?> UpdateAsync(Guid id, UpdateChannelRequest request, CancellationToken ct = default);
    Task<bool> InactivateAsync(Guid id, CancellationToken ct = default);
}

public class ChannelService(IOrganizationRepository repository) : IChannelService
{
    public async Task<IReadOnlyList<ChannelResponse>> GetAllAsync(Guid? establishmentId, bool? activeOnly, CancellationToken ct = default)
    {
        var items = await repository.GetChannelsAsync(establishmentId, activeOnly, ct);
        return items.Select(c => new ChannelResponse(c.Id, c.EstablishmentId, c.Type.ToString(), c.IsActive, c.CreatedAt)).ToList();
    }

    public async Task<ChannelResponse?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var channel = await repository.GetChannelByIdAsync(id, ct);
        return channel is null
            ? null
            : new ChannelResponse(channel.Id, channel.EstablishmentId, channel.Type.ToString(), channel.IsActive, channel.CreatedAt);
    }

    public async Task<ChannelResponse> CreateAsync(CreateChannelRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Type) || !Enum.TryParse<ChannelType>(request.Type.Trim(), true, out var channelType))
            throw new ArgumentException("Tipo de canal inválido. Use 'Physical' ou 'Virtual'.");

        // Validação de integridade: o estabelecimento pai deve existir e estar ativo
        var parentEstablishment = await repository.GetEstablishmentByIdAsync(request.EstablishmentId, ct);
        if (parentEstablishment is null || !parentEstablishment.IsActive)
            throw new InvalidOperationException("O estabelecimento informado não existe ou está inativo.");

        if (await repository.ExistsChannelByTypeInEstablishmentAsync(request.EstablishmentId, channelType, null, ct))
            throw new InvalidOperationException($"O estabelecimento já possui um canal do tipo '{channelType}'.");

        var channel = new Channel
        {
            Id = Guid.CreateVersion7(),
            EstablishmentId = request.EstablishmentId,
            Type = channelType,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var created = await repository.CreateChannelAsync(channel, ct);
        return new ChannelResponse(created.Id, created.EstablishmentId, created.Type.ToString(), created.IsActive, created.CreatedAt);
    }

    public async Task<ChannelResponse?> UpdateAsync(Guid id, UpdateChannelRequest request, CancellationToken ct = default)
    {
        var existing = await repository.GetChannelByIdAsync(id, ct);
        if (existing is null) return null;

        existing.IsActive = request.IsActive;

        var updated = await repository.UpdateChannelAsync(existing, ct);
        return new ChannelResponse(updated.Id, updated.EstablishmentId, updated.Type.ToString(), updated.IsActive, updated.CreatedAt);
    }

    public async Task<bool> InactivateAsync(Guid id, CancellationToken ct = default)
    {
        var existing = await repository.GetChannelByIdAsync(id, ct);
        if (existing is null) return false;

        existing.IsActive = false;
        await repository.UpdateChannelAsync(existing, ct);
        return true;
    }
}
