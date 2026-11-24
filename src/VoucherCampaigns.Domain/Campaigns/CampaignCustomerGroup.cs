using VoucherCampaigns.Domain.Customers;

namespace VoucherCampaigns.Domain.Campaigns;

/// <summary>Grupo eleito da campanha em lote (spec GH-072). A foto dos CPFs só existe na emissão futura.</summary>
public class CampaignCustomerGroup
{
    public Guid Id { get; set; }

    public Guid CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    public Guid GroupId { get; set; }

    public CustomerGroup? Group { get; set; }
}
