using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Application.Campaigns;

public interface ICampaignRepository
{
    Task AddAsync(Campaign campaign, CancellationToken cancellationToken);

    /// <summary>
    /// Campanhas da rede com os itens, das mais recentes para as mais antigas. Sem filtro de status:
    /// o filtro é pelo status efetivo, calculado com o relógio (PR-04).
    /// </summary>
    Task<IReadOnlyList<Campaign>> GetByNetworkAsync(Guid networkId, CancellationToken cancellationToken);

    /// <summary>Campanha com a rede (para o fuso) e os itens, ou null. Sem rastreamento.</summary>
    Task<Campaign?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Como <see cref="FindByIdAsync"/>, mas rastreada para alteração e gravação com <see cref="SaveChangesAsync"/>.</summary>
    Task<Campaign?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Carrega na campanha rastreada a lista de clientes específicos (spec GH-047, D-8), antes de substituí-la.</summary>
    Task LoadEligibleCustomersAsync(Campaign campaign, CancellationToken cancellationToken);

    /// <summary>CPFs da lista da campanha, em ordem, sem rastreamento (duplicação, D-18).</summary>
    Task<IReadOnlyList<string>> GetEligibleDocumentsAsync(Guid campaignId, CancellationToken cancellationToken);

    /// <summary>Página da lista de clientes específicos, em ordem de CPF, e o total (D-10).</summary>
    Task<(int Total, IReadOnlyList<string> Documents)> GetEligibleDocumentsPageAsync(
        Guid campaignId, int skip, int take, CancellationToken cancellationToken);

    /// <summary>Verdadeiro quando o CPF está na lista de clientes específicos da campanha.</summary>
    Task<bool> IsCustomerListedAsync(Guid campaignId, string document, CancellationToken cancellationToken);

    void AddStatusEvent(CampaignStatusEvent statusEvent);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
