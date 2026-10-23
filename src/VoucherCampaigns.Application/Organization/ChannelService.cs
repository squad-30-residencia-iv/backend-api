using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Organization;

public interface IChannelService
{
    Task<IReadOnlyList<ChannelResponse>> GetAllAsync(Guid? establishmentId, bool? activeOnly, CancellationToken cancellationToken = default);

    Task<ChannelResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ChannelResponse> CreateAsync(CreateChannelRequest request, CancellationToken cancellationToken = default);

    Task<ChannelResponse> UpdateAsync(Guid id, UpdateChannelRequest request, CancellationToken cancellationToken = default);

    Task InactivateAsync(Guid id, CancellationToken cancellationToken = default);
}

public class ChannelService(IOrganizationRepository repository, ICurrentUser currentUser, TimeProvider timeProvider) : IChannelService
{
    public async Task<IReadOnlyList<ChannelResponse>> GetAllAsync(Guid? establishmentId, bool? activeOnly, CancellationToken cancellationToken = default)
    {
        var scopedEstablishmentId = InputValues.RequiredId(
            establishmentId, nameof(CreateChannelRequest.EstablishmentId), OrganizationContract.EstablishmentRequired);
        await RequireEstablishmentInScopeAsync(scopedEstablishmentId, cancellationToken);

        var items = await repository.GetChannelsAsync(scopedEstablishmentId, activeOnly, cancellationToken);
        return items.Select(Map).ToList();
    }

    public async Task<ChannelResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var channel = await RequireInScopeAsync(id, cancellationToken);
        return Map(channel);
    }

    public async Task<ChannelResponse> CreateAsync(CreateChannelRequest request, CancellationToken cancellationToken = default)
    {
        var establishmentId = InputValues.RequiredId(
            request.EstablishmentId, nameof(request.EstablishmentId), OrganizationContract.EstablishmentRequired);
        var type = InputValues.ParseChannelType(request.Type);

        await RequireActiveEstablishmentAsync(establishmentId, cancellationToken);

        if (await repository.ExistsChannelByTypeInEstablishmentAsync(establishmentId, type, null, cancellationToken))
        {
            throw new BusinessRuleException(OrganizationErrors.ChannelTypeTakenCode, OrganizationErrors.ChannelTypeTakenMessage);
        }

        var channel = new Channel
        {
            Id = Guid.CreateVersion7(),
            EstablishmentId = establishmentId,
            Type = type,
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };

        return Map(await repository.CreateChannelAsync(channel, cancellationToken));
    }

    public async Task<ChannelResponse> UpdateAsync(Guid id, UpdateChannelRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await RequireInScopeAsync(id, cancellationToken);
        var isActive = InputValues.RequiredFlag(request.IsActive, nameof(request.IsActive), OrganizationContract.ChannelActiveRequired);

        if (isActive)
        {
            await RequireActiveEstablishmentAsync(existing.EstablishmentId, cancellationToken);
        }

        existing.IsActive = isActive;
        return Map(await repository.UpdateChannelAsync(existing, cancellationToken));
    }

    public async Task InactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await RequireInScopeAsync(id, cancellationToken);
        existing.IsActive = false;
        await repository.UpdateChannelAsync(existing, cancellationToken);
    }

    private async Task<Channel> RequireInScopeAsync(Guid id, CancellationToken cancellationToken)
    {
        var channel = await repository.GetChannelByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.ChannelNotFound);

        await RequireEstablishmentInScopeAsync(channel.EstablishmentId, cancellationToken);
        return channel;
    }

    private async Task<Establishment> RequireEstablishmentInScopeAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var establishment = await repository.GetEstablishmentByIdAsync(establishmentId, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.EstablishmentNotFound);

        var region = await repository.GetRegionByIdAsync(establishment.RegionId, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.RegionNotFound);

        NetworkScope.Ensure(currentUser, region.NetworkId);
        return establishment;
    }

    private async Task RequireActiveEstablishmentAsync(Guid establishmentId, CancellationToken cancellationToken)
    {
        var establishment = await RequireEstablishmentInScopeAsync(establishmentId, cancellationToken);

        if (!establishment.IsActive)
        {
            throw new BusinessRuleException(OrganizationErrors.ChannelParentUnavailableCode, OrganizationErrors.ChannelParentUnavailableMessage);
        }
    }

    private static ChannelResponse Map(Channel channel) =>
        new(channel.Id, channel.EstablishmentId, channel.Type.ToString(), channel.IsActive, channel.CreatedAt);
}
