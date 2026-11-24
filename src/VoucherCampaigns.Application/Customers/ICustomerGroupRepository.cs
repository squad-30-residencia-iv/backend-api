using VoucherCampaigns.Domain.Customers;

namespace VoucherCampaigns.Application.Customers;

public interface ICustomerGroupRepository
{
    Task<IReadOnlyList<CustomerGroup>> ListAsync(Guid networkId, bool? activeOnly, CancellationToken cancellationToken);

    Task<CustomerGroup?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<CustomerGroup?> FindWithMembersAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerGroup>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(Guid networkId, string name, Guid? exceptId, CancellationToken cancellationToken);

    Task AddAsync(CustomerGroup group, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task<(int Total, IReadOnlyList<string> Documents)> MembersPageAsync(Guid groupId, int skip, int take, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, int>> CountMembersAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken);

    Task<int> CountDistinctDocumentsAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, int>> CountDistinctByCampaignAsync(IReadOnlyCollection<Guid> campaignIds, CancellationToken cancellationToken);
}
