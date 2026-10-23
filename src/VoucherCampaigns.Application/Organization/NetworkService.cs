using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Organization;

public interface INetworkService
{
    Task<IReadOnlyList<NetworkResponse>> GetAllAsync(bool? activeOnly, CancellationToken cancellationToken = default);

    Task<NetworkResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<NetworkResponse> CreateAsync(CreateNetworkRequest request, CancellationToken cancellationToken = default);

    Task<NetworkResponse> UpdateAsync(Guid id, UpdateNetworkRequest request, CancellationToken cancellationToken = default);

    Task InactivateAsync(Guid id, CancellationToken cancellationToken = default);
}

public class NetworkService(IOrganizationRepository repository, ICurrentUser currentUser, TimeProvider timeProvider) : INetworkService
{
    public async Task<IReadOnlyList<NetworkResponse>> GetAllAsync(bool? activeOnly, CancellationToken cancellationToken = default)
    {
        // O administrador da plataforma enxerga todas as redes; os demais, só a do token.
        if (currentUser.IsPlatformAdmin)
        {
            var networks = await repository.GetNetworksAsync(activeOnly, cancellationToken);
            return networks.Select(Map).ToList();
        }

        var network = await repository.GetNetworkByIdAsync(currentUser.NetworkId, cancellationToken);

        if (network is null || !Matches(network.IsActive, activeOnly))
        {
            return [];
        }

        return [Map(network)];
    }

    public async Task<NetworkResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        NetworkScope.Ensure(currentUser, id);

        var network = await repository.GetNetworkByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.NetworkNotFound);

        return Map(network);
    }

    public async Task<NetworkResponse> CreateAsync(CreateNetworkRequest request, CancellationToken cancellationToken = default)
    {
        var name = Name(request.Name);
        var timeZone = TimeZone(request.TimeZone);

        if (await repository.ExistsNetworkByNameAsync(name, null, cancellationToken))
        {
            throw new BusinessRuleException(OrganizationErrors.NetworkNameTakenCode, OrganizationErrors.NetworkNameTakenMessage);
        }

        var network = new Network
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            TimeZone = timeZone,
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };

        return Map(await repository.CreateNetworkAsync(network, cancellationToken));
    }

    public async Task<NetworkResponse> UpdateAsync(Guid id, UpdateNetworkRequest request, CancellationToken cancellationToken = default)
    {
        NetworkScope.Ensure(currentUser, id);

        var existing = await repository.GetNetworkByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.NetworkNotFound);

        var name = Name(request.Name);
        var timeZone = TimeZone(request.TimeZone);
        var isActive = InputValues.RequiredFlag(request.IsActive, nameof(request.IsActive), OrganizationContract.NetworkActiveRequired);

        if (await repository.ExistsNetworkByNameAsync(name, id, cancellationToken))
        {
            throw new BusinessRuleException(OrganizationErrors.NetworkNameTakenCode, OrganizationErrors.NetworkNameTakenMessage);
        }

        existing.Name = name;
        existing.TimeZone = timeZone;
        existing.IsActive = isActive;

        return Map(await repository.UpdateNetworkAsync(existing, cancellationToken));
    }

    public async Task InactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        NetworkScope.Ensure(currentUser, id);

        var existing = await repository.GetNetworkByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.NetworkNotFound);

        existing.IsActive = false;
        await repository.UpdateNetworkAsync(existing, cancellationToken);
    }

    private static bool Matches(bool isActive, bool? activeOnly) =>
        activeOnly is null || isActive == activeOnly.Value;

    private static string Name(string value) =>
        InputValues.RequiredText(value, nameof(CreateNetworkRequest.Name), OrganizationContract.NetworkNameRequired, OrganizationContract.NameMaxLength, OrganizationContract.NameMaxLengthMessage);

    private static string TimeZone(string value) =>
        InputValues.TimeZone(
            value, OrganizationContract.TimeZoneRequired, OrganizationContract.TimeZoneMaxLengthMessage, OrganizationContract.TimeZoneInvalid);

    private static NetworkResponse Map(Network network) =>
        new(network.Id, network.Name, network.TimeZone, network.IsActive, network.CreatedAt);
}
