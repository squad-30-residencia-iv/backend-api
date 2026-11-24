namespace VoucherCampaigns.Domain.Customers;

/// <summary>CPF do grupo, só os 11 dígitos. O mesmo CPF pode estar em vários grupos.</summary>
public class CustomerGroupMember
{
    public Guid GroupId { get; set; }

    public CustomerGroup? Group { get; set; }

    public string Document { get; set; } = string.Empty;
}
