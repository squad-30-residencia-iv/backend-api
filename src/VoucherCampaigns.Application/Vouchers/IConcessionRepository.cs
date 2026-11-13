using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Application.Vouchers;

/// <summary>Resultado da reserva atômica de saldo (spec GH-045, D-4).</summary>
public enum ReserveOutcome
{
    Reserved,

    /// <summary>O status gravado não é agendada nem ativa: pausada, encerrada ou cancelada no meio da concessão (GH-061, D-9).</summary>
    CampaignNotActive,
    CampaignSoldOut,
    TargetSoldOut
}

/// <summary>Provisório com a reserva em dia, a descartar no cancelamento da campanha (GH-061, D-6).</summary>
public record LiveProvisionalVoucher(Guid VoucherId, Guid CampaignId, Guid CampaignItemId, Guid ConcessionId);

/// <summary>Reserva provisória vencida, a liberar (PR-17).</summary>
public record ExpiredReservation(Guid VoucherId, Guid CampaignId, Guid CampaignItemId, Guid ConcessionId);

/// <summary>Transação da concessão; descartar sem <see cref="CommitAsync"/> desfaz tudo.</summary>
public interface IConcessionTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Persistência do motor. As operações de saldo são comandos condicionais únicos, atômicos nos dois bancos:
/// nenhuma lê o contador para depois gravar.
/// </summary>
public interface IConcessionRepository
{
    /// <summary>Loja da rede, com os canais; null quando não existe ou é de outra rede.</summary>
    Task<Establishment?> FindEstablishmentAsync(Guid establishmentId, Guid networkId, CancellationToken cancellationToken);

    /// <summary>Concessão da transação na rede, com o voucher, a campanha (e a rede) e o alvo. Sem rastreamento.</summary>
    Task<Concession?> FindByTransactionAsync(Guid networkId, string transactionId, CancellationToken cancellationToken);

    /// <summary>Como <see cref="FindByTransactionAsync"/>, pelo id; null quando é de outra rede.</summary>
    Task<Concession?> FindByIdAsync(Guid id, Guid networkId, CancellationToken cancellationToken);

    /// <summary>Campanhas aprovadas da rede (status gravado fora de rascunho, aprovação e reprovada), com itens, condições, escopo e rede. Sem rastreamento.</summary>
    Task<IReadOnlyList<Campaign>> GetApprovedCampaignsAsync(Guid networkId, CancellationToken cancellationToken);

    /// <summary>Das campanhas informadas, as que têm o CPF na lista de clientes específicos (spec GH-047, D-8), numa consulta.</summary>
    Task<IReadOnlySet<Guid>> GetCampaignsListingCustomerAsync(
        IReadOnlyCollection<Guid> campaignIds, string customerDocument, CancellationToken cancellationToken);

    /// <summary>Vouchers não descartados do cliente na rede, contados por campanha (RF06c).</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountCustomerVouchersAsync(Guid networkId, string customerDocument, CancellationToken cancellationToken);

    /// <summary>Vouchers do cliente na rede, dos mais recentes para os mais antigos, com campanha, alvo e concessão.</summary>
    Task<IReadOnlyList<Voucher>> GetCustomerVouchersAsync(Guid networkId, string customerDocument, CancellationToken cancellationToken);

    /// <summary>Provisórios com a reserva vencida; de uma rede ou de todas.</summary>
    Task<IReadOnlyList<ExpiredReservation>> GetExpiredReservationsAsync(Guid? networkId, DateTime utcNow, CancellationToken cancellationToken);

    Task<IConcessionTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reserva uma unidade do saldo da campanha e da cota do alvo: <c>UPDATE ... WHERE issued + reserved &lt; limit</c>.
    /// Deve ser o primeiro comando da transação, para serializar as concessões da mesma campanha.
    /// </summary>
    Task<ReserveOutcome> TryReserveAsync(Guid campaignId, Guid campaignItemId, CancellationToken cancellationToken);

    /// <summary>Status gravado da campanha, ou null se ela não existe.</summary>
    Task<CampaignStatus?> GetCampaignStatusAsync(Guid campaignId, CancellationToken cancellationToken);

    /// <summary>Provisórios da campanha com a reserva em dia (GH-061, D-6).</summary>
    Task<IReadOnlyList<LiveProvisionalVoucher>> GetLiveProvisionalAsync(Guid campaignId, DateTime utcNow, CancellationToken cancellationToken);

    /// <summary>
    /// Leva a <c>Cancelled</c>, num comando só, os vouchers <c>Available</c> da campanha dentro da validade, com
    /// <c>cancelled_at</c> (GH-061, D-5, D-7 e D-12). Devolve quantos mudaram.
    /// </summary>
    Task<int> CancelAvailableVouchersAsync(Guid campaignId, DateTime utcNow, CancellationToken cancellationToken);

    /// <summary>Ids dos vouchers <c>Cancelled</c> da campanha, para o evento de cada um (GH-061, D-8).</summary>
    Task<IReadOnlyList<Guid>> GetCancelledVoucherIdsAsync(Guid campaignId, CancellationToken cancellationToken);

    /// <summary>Provisório com a reserva ainda válida vira disponível; falso quando outro pedido chegou antes ou a reserva venceu.</summary>
    Task<bool> TryConfirmVoucherAsync(Guid voucherId, DateTime utcNow, CancellationToken cancellationToken);

    /// <summary>Provisório vira descartado; falso quando já não estava provisório.</summary>
    Task<bool> TryDiscardVoucherAsync(Guid voucherId, DateTime utcNow, CancellationToken cancellationToken);

    /// <summary>Reserva consumida em definitivo: <c>reserved - 1</c>, <c>issued + 1</c>, na campanha e no alvo.</summary>
    Task ConsumeReservationAsync(Guid campaignId, Guid campaignItemId, CancellationToken cancellationToken);

    /// <summary>Reserva devolvida: <c>reserved - 1</c>, na campanha e no alvo.</summary>
    Task ReleaseReservationAsync(Guid campaignId, Guid campaignItemId, CancellationToken cancellationToken);

    Task MarkConcessionAsync(Guid concessionId, ConcessionStatus status, DateTime decidedAt, CancellationToken cancellationToken);

    void Add(Concession concession);

    void Add(Voucher voucher);

    void Add(VoucherEvent voucherEvent);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Falso quando a transação já existe na rede (índice único): outro pedido idêntico chegou antes.</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);
}
