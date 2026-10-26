namespace VoucherCampaigns.Domain.Organization;

/// <summary>Estabelecimento (loja), ligado a uma região.</summary>
public class Establishment
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid RegionId { get; set; }

    public Region? Region { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Canais que a loja opera. O canal é atributo do estabelecimento, não nó da hierarquia (PR-11).</summary>
    public ICollection<Channel> Channels { get; set; } = [];

    /// <summary>Tipos de canal habilitados, em ordem fixa (físico, virtual).</summary>
    public IReadOnlyList<ChannelType> EnabledChannels() =>
        Channels.Where(channel => channel.IsActive).Select(channel => channel.Type).Distinct().Order().ToList();

    /// <summary>
    /// Deixa habilitados exatamente os tipos informados. O tipo que sai é desabilitado e o que volta reaproveita
    /// a linha existente, porque o par loja + tipo é único.
    /// </summary>
    public void EnableChannels(IReadOnlyCollection<ChannelType> types, DateTime now)
    {
        foreach (var channel in Channels)
        {
            channel.IsActive = types.Contains(channel.Type);
        }

        foreach (var type in types.Where(type => Channels.All(channel => channel.Type != type)))
        {
            Channels.Add(new Channel
            {
                Id = Guid.CreateVersion7(),
                EstablishmentId = Id,
                Type = type,
                IsActive = true,
                CreatedAt = now
            });
        }
    }
}
