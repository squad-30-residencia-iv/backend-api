using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Application.Campaigns;

public interface ICampaignRepository
{
    Task AddAsync(Campaign campaign, CancellationToken cancellationToken);

    /// <summary>Campanhas da rede, das mais recentes para as mais antigas.</summary>
    Task<IReadOnlyList<Campaign>> GetByNetworkAsync(Guid networkId, CampaignStatus? status, CancellationToken cancellationToken);

    /// <summary>Campanha com a rede carregada (para o fuso), ou null.</summary>
    Task<Campaign?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
}
