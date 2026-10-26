using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Organization;

public interface IEstablishmentService
{
    Task<IReadOnlyList<EstablishmentResponse>> GetAllAsync(Guid? regionId, bool? activeOnly, CancellationToken cancellationToken = default);

    Task<EstablishmentResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<EstablishmentResponse> CreateAsync(CreateEstablishmentRequest request, CancellationToken cancellationToken = default);

    Task<EstablishmentResponse> UpdateAsync(Guid id, UpdateEstablishmentRequest request, CancellationToken cancellationToken = default);

    Task InactivateAsync(Guid id, CancellationToken cancellationToken = default);
}

public class EstablishmentService(IOrganizationRepository repository, ICurrentUser currentUser, TimeProvider timeProvider) : IEstablishmentService
{
    public async Task<IReadOnlyList<EstablishmentResponse>> GetAllAsync(Guid? regionId, bool? activeOnly, CancellationToken cancellationToken = default)
    {
        var scopedRegionId = InputValues.RequiredId(regionId, nameof(CreateEstablishmentRequest.RegionId), OrganizationContract.RegionRequired);

        var region = await repository.GetRegionByIdAsync(scopedRegionId, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.RegionNotFound);
        NetworkScope.Ensure(currentUser, region.NetworkId);

        var items = await repository.GetEstablishmentsAsync(scopedRegionId, activeOnly, cancellationToken);
        return items.Select(Map).ToList();
    }

    public async Task<EstablishmentResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var establishment = await RequireInScopeAsync(id, cancellationToken);
        return Map(establishment);
    }

    public async Task<EstablishmentResponse> CreateAsync(CreateEstablishmentRequest request, CancellationToken cancellationToken = default)
    {
        var regionId = InputValues.RequiredId(request.RegionId, nameof(request.RegionId), OrganizationContract.RegionRequired);
        var code = Code(request.Code);
        var name = Name(request.Name);
        var channels = InputValues.ChannelTypes(request.Channels);

        // US-02.1, cenário 2: região inexistente no corpo recusa o cadastro com a mensagem do backlog.
        var region = await repository.GetRegionByIdAsync(regionId, cancellationToken)
            ?? throw new BusinessRuleException(
                OrganizationErrors.EstablishmentRegionNotFoundCode, OrganizationErrors.EstablishmentRegionNotFoundMessage);
        NetworkScope.Ensure(currentUser, region.NetworkId);
        RequireActive(region);

        if (await repository.ExistsEstablishmentByCodeInRegionAsync(regionId, code, null, cancellationToken))
        {
            throw new BusinessRuleException(OrganizationErrors.EstablishmentCodeTakenCode, OrganizationErrors.EstablishmentCodeTakenMessage);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var establishment = new Establishment
        {
            Id = Guid.CreateVersion7(),
            RegionId = regionId,
            Code = code,
            Name = name,
            IsActive = true,
            CreatedAt = now
        };
        establishment.EnableChannels(channels, now);

        return Map(await repository.CreateEstablishmentAsync(establishment, cancellationToken));
    }

    public async Task<EstablishmentResponse> UpdateAsync(Guid id, UpdateEstablishmentRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await RequireInScopeAsync(id, cancellationToken);
        var code = Code(request.Code);
        var name = Name(request.Name);
        var isActive = InputValues.RequiredFlag(request.IsActive, nameof(request.IsActive), OrganizationContract.EstablishmentActiveRequired);
        var channels = InputValues.ChannelTypes(request.Channels);

        if (isActive)
        {
            var region = await repository.GetRegionByIdAsync(existing.RegionId, cancellationToken)
                ?? throw new ResourceNotFoundException(OrganizationContract.RegionNotFound);
            RequireActive(region);
        }

        if (await repository.ExistsEstablishmentByCodeInRegionAsync(existing.RegionId, code, id, cancellationToken))
        {
            throw new BusinessRuleException(OrganizationErrors.EstablishmentCodeTakenCode, OrganizationErrors.EstablishmentCodeTakenMessage);
        }

        existing.Code = code;
        existing.Name = name;
        existing.IsActive = isActive;
        existing.EnableChannels(channels, timeProvider.GetUtcNow().UtcDateTime);

        return Map(await repository.UpdateEstablishmentAsync(existing, cancellationToken));
    }

    public async Task InactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await RequireInScopeAsync(id, cancellationToken);
        existing.IsActive = false;
        await repository.UpdateEstablishmentAsync(existing, cancellationToken);
    }

    private async Task<Establishment> RequireInScopeAsync(Guid id, CancellationToken cancellationToken)
    {
        var establishment = await repository.GetEstablishmentByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.EstablishmentNotFound);

        var region = await repository.GetRegionByIdAsync(establishment.RegionId, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.RegionNotFound);

        NetworkScope.Ensure(currentUser, region.NetworkId);
        return establishment;
    }

    private static void RequireActive(Region region)
    {
        if (!region.IsActive)
        {
            throw new BusinessRuleException(
                OrganizationErrors.EstablishmentRegionInactiveCode, OrganizationErrors.EstablishmentRegionInactiveMessage);
        }
    }

    private static string Code(string value) =>
        InputValues.RequiredText(
            value, nameof(CreateEstablishmentRequest.Code), OrganizationContract.EstablishmentCodeRequired,
            OrganizationContract.EstablishmentCodeMaxLength, OrganizationContract.EstablishmentCodeMaxLengthMessage);

    private static string Name(string value) =>
        InputValues.RequiredText(
            value, nameof(CreateEstablishmentRequest.Name), OrganizationContract.EstablishmentNameRequired,
            OrganizationContract.NameMaxLength, OrganizationContract.NameMaxLengthMessage);

    private static EstablishmentResponse Map(Establishment establishment) =>
        new(
            establishment.Id,
            establishment.RegionId,
            establishment.Code,
            establishment.Name,
            establishment.EnabledChannels().Select(type => type.ToString()).ToList(),
            establishment.IsActive,
            establishment.CreatedAt);
}
