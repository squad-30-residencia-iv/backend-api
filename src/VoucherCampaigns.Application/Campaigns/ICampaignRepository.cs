using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Application.Campaigns;

public interface ICampaignRepository
{
    Task AddAsync(Campaign campaign, CancellationToken cancellationToken);

    /// <summary>Campanhas da rede com os itens, das mais recentes para as mais antigas.</summary>
    Task<IReadOnlyList<Campaign>> GetByNetworkAsync(Guid networkId, CampaignStatus? status, CancellationToken cancellationToken);

    /// <summary>Campanha com a rede (para o fuso) e os itens, ou null. Sem rastreamento.</summary>
    Task<Campaign?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Como <see cref="FindByIdAsync"/>, mas rastreada para alteração e gravação com <see cref="SaveChangesAsync"/>.</summary>
    Task<Campaign?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
