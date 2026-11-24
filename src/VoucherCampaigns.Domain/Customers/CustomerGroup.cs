namespace VoucherCampaigns.Domain.Customers;

/// <summary>Grupo de CPFs de uma rede, reutilizado por campanhas em lote (spec GH-072). A emissão fica para depois.</summary>
public class CustomerGroup
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid NetworkId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CustomerGroupMember> Members { get; set; } = [];
}
