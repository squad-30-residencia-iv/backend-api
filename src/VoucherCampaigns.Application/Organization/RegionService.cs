using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Organization;

public interface IRegionService
{
    Task<IReadOnlyList<RegionResponse>> GetAllAsync(Guid? networkId, bool? activeOnly, CancellationToken cancellationToken = default);

    Task<RegionResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<RegionResponse> CreateAsync(CreateRegionRequest request, CancellationToken cancellationToken = default);

    Task<RegionResponse> UpdateAsync(Guid id, UpdateRegionRequest request, CancellationToken cancellationToken = default);

    Task InactivateAsync(Guid id, CancellationToken cancellationToken = default);
}

public class RegionService(IOrganizationRepository repository, ICurrentUser currentUser, TimeProvider timeProvider) : IRegionService
{
    public async Task<IReadOnlyList<RegionResponse>> GetAllAsync(Guid? networkId, bool? activeOnly, CancellationToken cancellationToken = default)
    {
        var scopedNetworkId = InputValues.RequiredId(networkId, nameof(CreateRegionRequest.NetworkId), OrganizationContract.NetworkRequired);
        NetworkScope.Ensure(currentUser, scopedNetworkId);

        var items = await repository.GetRegionsAsync(scopedNetworkId, activeOnly, cancellationToken);
        return items.Select(Map).ToList();
    }

    public async Task<RegionResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var region = await RequireInScopeAsync(id, cancellationToken);
        return Map(region);
    }

    public async Task<RegionResponse> CreateAsync(CreateRegionRequest request, CancellationToken cancellationToken = default)
    {
        var networkId = InputValues.RequiredId(request.NetworkId, nameof(request.NetworkId), OrganizationContract.NetworkRequired);
        var name = Name(request.Name);

        NetworkScope.Ensure(currentUser, networkId);
        await RequireActiveNetworkAsync(networkId, cancellationToken);

        if (await repository.ExistsRegionByNameInNetworkAsync(networkId, name, null, cancellationToken))
        {
            throw new BusinessRuleException(OrganizationErrors.RegionNameTakenCode, OrganizationErrors.RegionNameTakenMessage);
        }

        var region = new Region
        {
            Id = Guid.CreateVersion7(),
            NetworkId = networkId,
            Name = name,
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };

        return Map(await repository.CreateRegionAsync(region, cancellationToken));
    }

    public async Task<RegionResponse> UpdateAsync(Guid id, UpdateRegionRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await RequireInScopeAsync(id, cancellationToken);
        var name = Name(request.Name);
        var isActive = InputValues.RequiredFlag(request.IsActive, nameof(request.IsActive), OrganizationContract.RegionActiveRequired);

        if (isActive)
        {
            await RequireActiveNetworkAsync(existing.NetworkId, cancellationToken);
        }

        if (await repository.ExistsRegionByNameInNetworkAsync(existing.NetworkId, name, id, cancellationToken))
        {
            throw new BusinessRuleException(OrganizationErrors.RegionNameTakenCode, OrganizationErrors.RegionNameTakenMessage);
        }

        existing.Name = name;
        existing.IsActive = isActive;

        return Map(await repository.UpdateRegionAsync(existing, cancellationToken));
    }

    public async Task InactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await RequireInScopeAsync(id, cancellationToken);
        existing.IsActive = false;
        await repository.UpdateRegionAsync(existing, cancellationToken);
    }

    private async Task<Region> RequireInScopeAsync(Guid id, CancellationToken cancellationToken)
    {
        var region = await repository.GetRegionByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.RegionNotFound);

        NetworkScope.Ensure(currentUser, region.NetworkId);
        return region;
    }

    private async Task RequireActiveNetworkAsync(Guid networkId, CancellationToken cancellationToken)
    {
        var network = await repository.GetNetworkByIdAsync(networkId, cancellationToken);

        if (network is null || !network.IsActive)
        {
            throw new BusinessRuleException(CampaignErrors.NetworkUnavailableCode, CampaignErrors.NetworkUnavailableMessage);
        }
    }

    private static string Name(string value) =>
        InputValues.RequiredText(value, nameof(CreateRegionRequest.Name), OrganizationContract.RegionNameRequired, OrganizationContract.NameMaxLength, OrganizationContract.NameMaxLengthMessage);

    private static RegionResponse Map(Region region) =>
        new(region.Id, region.NetworkId, region.Name, region.IsActive, region.CreatedAt);
}
