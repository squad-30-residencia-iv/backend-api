using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Domain.Campaigns;

/// <summary>
/// Onde a campanha vale: sem região e sem estabelecimento = rede inteira;
/// com região = toda a região; com estabelecimento = só aquela loja.
/// </summary>
public class CampaignScope
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    public Guid? RegionId { get; set; }

    public Region? Region { get; set; }

    public Guid? EstablishmentId { get; set; }

    public Establishment? Establishment { get; set; }

    public ChannelType ChannelType { get; set; }
}
