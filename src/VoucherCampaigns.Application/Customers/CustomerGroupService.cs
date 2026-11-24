using VoucherCampaigns.Application.Audit;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Application.Eligibility;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Customers;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Customers;

public interface ICustomerGroupService
{
    Task<IReadOnlyList<CustomerGroupResponse>> ListAsync(Guid? networkId, bool? activeOnly, CancellationToken cancellationToken);

    Task<CustomerGroupResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<CustomerGroupResponse> CreateAsync(CreateCustomerGroupRequest request, CancellationToken cancellationToken);

    Task<CustomerGroupResponse> UpdateAsync(Guid id, UpdateCustomerGroupRequest request, CancellationToken cancellationToken);

    Task InactivateAsync(Guid id, CancellationToken cancellationToken);

    Task<CustomerGroupResponse> ReplaceMembersAsync(Guid id, ReplaceCustomerGroupMembersRequest request, CancellationToken cancellationToken);

    Task<CustomerGroupMembersResponse> MembersAsync(Guid id, int? page, int? pageSize, CancellationToken cancellationToken);

    Task<PreviewCustomerGroupsResponse> PreviewAsync(PreviewCustomerGroupsRequest request, CancellationToken cancellationToken);

    /// <summary>União dos grupos ativos da rede. Grupo inativo, ausente ou de outra rede recusa.</summary>
    Task<int> CountActiveAsync(Guid networkId, IReadOnlyList<Guid> groupIds, CancellationToken cancellationToken);
}

public class CustomerGroupService(
    ICustomerGroupRepository groups,
    INetworkRepository networks,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider timeProvider) : ICustomerGroupService
{
    public async Task<IReadOnlyList<CustomerGroupResponse>> ListAsync(Guid? networkId, bool? activeOnly, CancellationToken cancellationToken)
    {
        var scoped = InputValues.RequiredId(networkId, nameof(networkId), CustomerGroupContract.NetworkRequired);
        NetworkScope.Ensure(currentUser, scoped);
        _ = await networks.FindByIdAsync(scoped, cancellationToken) ?? throw new ResourceNotFoundException(OrganizationContract.NetworkNotFound);
        var items = await groups.ListAsync(scoped, activeOnly, cancellationToken);
        var counts = await groups.CountMembersAsync(items.Select(item => item.Id).ToList(), cancellationToken);
        return items.Select(item => Map(item, counts.GetValueOrDefault(item.Id))).ToList();
    }

    public async Task<CustomerGroupResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var group = await RequireAsync(id, cancellationToken);
        return await MapAsync(group, cancellationToken);
    }

    public async Task<CustomerGroupResponse> CreateAsync(CreateCustomerGroupRequest request, CancellationToken cancellationToken)
    {
        var networkId = InputValues.RequiredId(request.NetworkId, nameof(request.NetworkId), CustomerGroupContract.NetworkRequired);
        var name = Name(request.Name);
        NetworkScope.Ensure(currentUser, networkId);
        await RequireActiveNetworkAsync(networkId, cancellationToken);
        if (await groups.NameExistsAsync(networkId, name, null, cancellationToken))
        {
            throw new BusinessRuleException(CustomerGroupErrors.NameTakenCode, CustomerGroupErrors.NameTakenMessage);
        }

        var group = new CustomerGroup
        {
            Id = Guid.CreateVersion7(),
            NetworkId = networkId,
            Name = name,
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        };
        await groups.AddAsync(group, cancellationToken);
        return Map(group, 0);
    }

    public async Task<CustomerGroupResponse> UpdateAsync(Guid id, UpdateCustomerGroupRequest request, CancellationToken cancellationToken)
    {
        var group = await RequireAsync(id, cancellationToken);
        var name = Name(request.Name);
        var active = InputValues.RequiredFlag(request.IsActive, nameof(request.IsActive), CustomerGroupContract.ActiveRequired);
        if (active)
        {
            await RequireActiveNetworkAsync(group.NetworkId, cancellationToken);
        }

        if (await groups.NameExistsAsync(group.NetworkId, name, group.Id, cancellationToken))
        {
            throw new BusinessRuleException(CustomerGroupErrors.NameTakenCode, CustomerGroupErrors.NameTakenMessage);
        }

        group.Name = name;
        group.IsActive = active;
        await groups.SaveChangesAsync(cancellationToken);
        return await MapAsync(group, cancellationToken);
    }

    public async Task InactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var group = await RequireAsync(id, cancellationToken);
        group.IsActive = false;
        await groups.SaveChangesAsync(cancellationToken);
    }

    public async Task<CustomerGroupResponse> ReplaceMembersAsync(Guid id, ReplaceCustomerGroupMembersRequest request, CancellationToken cancellationToken)
    {
        var documents = ParseDocuments(request.Documents);
        var group = await groups.FindWithMembersAsync(id, cancellationToken) ?? throw new ResourceNotFoundException(CustomerGroupErrors.NotFoundMessage);
        NetworkScope.Ensure(currentUser, group.NetworkId);

        var before = group.Members.Select(member => member.Document).ToHashSet(StringComparer.Ordinal);
        var after = documents.ToHashSet(StringComparer.Ordinal);
        var added = after.Count(document => !before.Contains(document));
        var removed = before.Count(document => !after.Contains(document));

        group.Members.Clear();
        foreach (var document in documents)
        {
            group.Members.Add(new CustomerGroupMember { GroupId = group.Id, Document = document });
        }

        if (added > 0 || removed > 0)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            audit.Append(new AuditEntry
            {
                Id = AuditIds.Next(now),
                EventType = AuditEventTypes.CampaignUpdated,
                ActorType = AuditActorType.User,
                ActorId = currentUser.UserId,
                NetworkId = group.NetworkId,
                EntityType = nameof(CustomerGroup),
                EntityId = group.Id,
                OccurredAt = now,
                Payload = AuditJson.Serialize(new { memberCount = documents.Count, added, removed }),
            });
        }

        await groups.SaveChangesAsync(cancellationToken);
        return Map(group, documents.Count);
    }

    public async Task<CustomerGroupMembersResponse> MembersAsync(Guid id, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var number = page ?? 1;
        var size = pageSize ?? EligibilityContract.DefaultCustomersPageSize;
        if (number < 1)
        {
            throw new RequestValidationException("page", EligibilityContract.PageInvalid);
        }

        if (size is < 1 or > EligibilityContract.MaxCustomersPageSize)
        {
            throw new RequestValidationException("pageSize", EligibilityContract.PageSizeInvalid);
        }

        var group = await RequireAsync(id, cancellationToken);
        var (total, documents) = await groups.MembersPageAsync(group.Id, (number - 1) * size, size, cancellationToken);
        return new CustomerGroupMembersResponse(number, size, total, documents);
    }

    public async Task<PreviewCustomerGroupsResponse> PreviewAsync(PreviewCustomerGroupsRequest request, CancellationToken cancellationToken)
    {
        var networkId = InputValues.RequiredId(request.NetworkId, nameof(request.NetworkId), CustomerGroupContract.NetworkRequired);
        NetworkScope.Ensure(currentUser, networkId);
        var ids = (request.GroupIds ?? []).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new PreviewCustomerGroupsResponse(0);
        }

        await EnsureActiveInNetworkAsync(networkId, ids, cancellationToken);
        return new PreviewCustomerGroupsResponse(await groups.CountDistinctDocumentsAsync(ids, cancellationToken));
    }

    public async Task<int> CountActiveAsync(Guid networkId, IReadOnlyList<Guid> groupIds, CancellationToken cancellationToken)
    {
        if (groupIds.Count == 0)
        {
            return 0;
        }

        await EnsureActiveInNetworkAsync(networkId, groupIds, cancellationToken);
        return await groups.CountDistinctDocumentsAsync(groupIds, cancellationToken);
    }

    private async Task EnsureActiveInNetworkAsync(Guid networkId, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var found = await groups.FindManyAsync(ids, cancellationToken);
        foreach (var id in ids)
        {
            var group = found.FirstOrDefault(item => item.Id == id);
            if (group is null)
            {
                throw new BusinessRuleException(CustomerGroupErrors.NotFoundCode, CustomerGroupErrors.NotFoundMessage);
            }

            if (group.NetworkId != networkId)
            {
                throw new ScopeForbiddenException();
            }

            if (!group.IsActive)
            {
                throw new BusinessRuleException(CustomerGroupErrors.InactiveCode, CustomerGroupErrors.InactiveMessage);
            }
        }
    }

    private async Task<CustomerGroup> RequireAsync(Guid id, CancellationToken cancellationToken)
    {
        var group = await groups.FindAsync(id, cancellationToken) ?? throw new ResourceNotFoundException(CustomerGroupErrors.NotFoundMessage);
        NetworkScope.Ensure(currentUser, group.NetworkId);
        return group;
    }

    private async Task RequireActiveNetworkAsync(Guid networkId, CancellationToken cancellationToken)
    {
        var network = await networks.FindByIdAsync(networkId, cancellationToken);
        if (network is null)
        {
            throw new ResourceNotFoundException(OrganizationContract.NetworkNotFound);
        }

        if (!network.IsActive)
        {
            throw new BusinessRuleException(CustomerGroupErrors.NetworkInactiveCode, CustomerGroupErrors.NetworkInactiveMessage);
        }
    }

    private static string Name(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new RequestValidationException(nameof(CreateCustomerGroupRequest.Name), CustomerGroupContract.NameRequired);
        }

        return trimmed;
    }

    private static List<string> ParseDocuments(IReadOnlyList<string?>? values)
    {
        if (values is null || values.Count == 0)
        {
            throw new RequestValidationException(nameof(ReplaceCustomerGroupMembersRequest.Documents), EligibilityContract.DocumentsRequired);
        }

        if (values.Count > EligibilityContract.MaxDocuments)
        {
            throw new RequestValidationException(nameof(ReplaceCustomerGroupMembersRequest.Documents), EligibilityContract.DocumentsTooMany);
        }

        var documents = new List<string>();
        var invalid = new List<int>();
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index] ?? string.Empty;
            var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
            var others = value.Count(character => !char.IsAsciiDigit(character) && character is not ('.' or '-' or ' '));
            if (others > 0 || !Cpf.IsValid(digits))
            {
                invalid.Add(index + 1);
                continue;
            }

            documents.Add(digits);
        }

        if (invalid.Count > 0)
        {
            throw new RequestValidationException(
                nameof(ReplaceCustomerGroupMembersRequest.Documents),
                string.Format(EligibilityContract.DocumentsInvalid, string.Join(", ", invalid)));
        }

        return documents.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    private async Task<CustomerGroupResponse> MapAsync(CustomerGroup group, CancellationToken cancellationToken)
    {
        var counts = await groups.CountMembersAsync([group.Id], cancellationToken);
        return Map(group, counts.GetValueOrDefault(group.Id));
    }

    private static CustomerGroupResponse Map(CustomerGroup group, int memberCount) =>
        new(group.Id, group.NetworkId, group.Name, group.IsActive, memberCount, DateTime.SpecifyKind(group.CreatedAt, DateTimeKind.Utc));
}
