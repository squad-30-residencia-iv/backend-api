using System.ComponentModel.DataAnnotations;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Eligibility;

namespace VoucherCampaigns.Application.Customers;

public static class CustomerGroupContract
{
    public const int NameMaxLength = 150;
    public const string NameRequired = "Informe o nome do grupo.";
    public const string NameLength = "O nome do grupo deve ter no máximo 150 caracteres.";
    public const string NetworkRequired = "Informe a rede do grupo.";
    public const string ActiveRequired = "Informe se o grupo está ativo.";
    public const string GroupIdsRequired = "Informe os grupos.";
}

public record CreateCustomerGroupRequest(
    [Required(ErrorMessage = CustomerGroupContract.NetworkRequired)] Guid? NetworkId,
    [Required(ErrorMessage = CustomerGroupContract.NameRequired)]
    [StringLength(CustomerGroupContract.NameMaxLength, MinimumLength = 1, ErrorMessage = CustomerGroupContract.NameLength)]
    string? Name) : INetworkScopedRequest
{
    Guid INetworkScopedRequest.NetworkId => NetworkId ?? Guid.Empty;
}

public record UpdateCustomerGroupRequest(
    [Required(ErrorMessage = CustomerGroupContract.NameRequired)]
    [StringLength(CustomerGroupContract.NameMaxLength, MinimumLength = 1, ErrorMessage = CustomerGroupContract.NameLength)]
    string? Name,
    [Required(ErrorMessage = CustomerGroupContract.ActiveRequired)] bool? IsActive);

public record CustomerGroupResponse(Guid Id, Guid NetworkId, string Name, bool IsActive, int MemberCount, DateTime CreatedAt);

public record ReplaceCustomerGroupMembersRequest(
    [Required(ErrorMessage = EligibilityContract.DocumentsRequired)]
    [MinLength(1, ErrorMessage = EligibilityContract.DocumentsRequired)]
    [MaxLength(EligibilityContract.MaxDocuments, ErrorMessage = EligibilityContract.DocumentsTooMany)]
    IReadOnlyList<string?>? Documents);

public record CustomerGroupMembersResponse(int Page, int PageSize, int Total, IReadOnlyList<string> Documents);

public record PreviewCustomerGroupsRequest(
    [Required(ErrorMessage = CustomerGroupContract.NetworkRequired)] Guid? NetworkId,
    IReadOnlyList<Guid?>? GroupIds) : INetworkScopedRequest
{
    Guid INetworkScopedRequest.NetworkId => NetworkId ?? Guid.Empty;
}

public record PreviewCustomerGroupsResponse(int UniqueCustomerCount);

public record ReplaceCampaignCustomerGroupsRequest(IReadOnlyList<Guid?>? GroupIds);

public record CampaignCustomerGroupResponse(Guid Id, string Name);
