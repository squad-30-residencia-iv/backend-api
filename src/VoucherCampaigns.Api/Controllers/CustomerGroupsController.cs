using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Api.Filters;
using VoucherCampaigns.Application.Customers;

namespace VoucherCampaigns.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.ReadHierarchy)]
[Route("api/v1/customer-groups")]
public class CustomerGroupsController(ICustomerGroupService service) : ControllerBase
{
    [HttpGet]
    [ValidateTenantScope]
    public async Task<ActionResult<IReadOnlyList<CustomerGroupResponse>>> List(
        [FromQuery] Guid? networkId, [FromQuery] bool? activeOnly, CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(networkId, activeOnly, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerGroupResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.ManageHierarchy)]
    [ValidateTenantScope]
    public async Task<ActionResult<CustomerGroupResponse>> Create(
        [FromBody] CreateCustomerGroupRequest request, CancellationToken cancellationToken)
    {
        var created = await service.CreateAsync(request, cancellationToken);
        return Created($"/api/v1/customer-groups/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManageHierarchy)]
    public async Task<ActionResult<CustomerGroupResponse>> Update(
        Guid id, [FromBody] UpdateCustomerGroupRequest request, CancellationToken cancellationToken) =>
        Ok(await service.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManageHierarchy)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await service.InactivateAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/members")]
    [Authorize(Policy = AuthorizationPolicies.ManageHierarchy)]
    public async Task<ActionResult<CustomerGroupResponse>> ReplaceMembers(
        Guid id, [FromBody] ReplaceCustomerGroupMembersRequest request, CancellationToken cancellationToken) =>
        Ok(await service.ReplaceMembersAsync(id, request, cancellationToken));

    [HttpGet("{id:guid}/members")]
    [Authorize(Policy = AuthorizationPolicies.ManageHierarchy)]
    public async Task<ActionResult<CustomerGroupMembersResponse>> Members(
        Guid id, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var result = await service.MembersAsync(id, page, pageSize, cancellationToken);
        Response.Headers[PagingHeaders.TotalCount] = result.Total.ToString(CultureInfo.InvariantCulture);
        return Ok(result);
    }

    [HttpPost("preview")]
    [ValidateTenantScope]
    public async Task<ActionResult<PreviewCustomerGroupsResponse>> Preview(
        [FromBody] PreviewCustomerGroupsRequest request, CancellationToken cancellationToken) =>
        Ok(await service.PreviewAsync(request, cancellationToken));
}
