using System.ComponentModel.DataAnnotations;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Application.Vouchers;

/// <summary>Mensagens de contrato das rotas de resgate (spec GH-049, 2.3); as da compra vêm de <see cref="ConcessionContract"/>.</summary>
public static class RedemptionContract
{
    public const string VoucherIdRequired = "Informe o voucher a resgatar.";
}

/// <summary>Verificação dos vouchers aplicáveis à compra (US-05.1, US-07.3 cenário 1): a mesma compra da concessão (D-3).</summary>
public record RedemptionCheckRequest(
    [Required(ErrorMessage = ConcessionContract.TransactionIdRequired)]
    [StringLength(ConcessionContract.MaxTransactionIdLength, ErrorMessage = ConcessionContract.TransactionIdTooLong)]
    string? TransactionId,
    [Required(ErrorMessage = ConcessionContract.CustomerRequired)] ConcessionCustomerRequest? Customer,
    [Required(ErrorMessage = ConcessionContract.EstablishmentRequired)] Guid? EstablishmentId,
    [Required(ErrorMessage = ConcessionContract.ChannelRequired)] string? Channel,
    [Required(ErrorMessage = ConcessionContract.TotalAmountRequired)] decimal? TotalAmount,
    [Required(ErrorMessage = ConcessionContract.ItemsRequired)]
    [MinLength(1, ErrorMessage = ConcessionContract.ItemsRequired)]
    [MaxLength(ConcessionContract.MaxItems, ErrorMessage = ConcessionContract.ItemsTooMany)]
    IReadOnlyList<PurchaseItemRequest?>? Items) : IPurchaseRequest;

/// <summary>Resgate do voucher escolhido pelo cliente (US-07.3 cenário 2), na mesma compra da verificação.</summary>
public record RedemptionRequest(
    [Required(ErrorMessage = RedemptionContract.VoucherIdRequired)] Guid? VoucherId,
    [Required(ErrorMessage = ConcessionContract.TransactionIdRequired)]
    [StringLength(ConcessionContract.MaxTransactionIdLength, ErrorMessage = ConcessionContract.TransactionIdTooLong)]
    string? TransactionId,
    [Required(ErrorMessage = ConcessionContract.CustomerRequired)] ConcessionCustomerRequest? Customer,
    [Required(ErrorMessage = ConcessionContract.EstablishmentRequired)] Guid? EstablishmentId,
    [Required(ErrorMessage = ConcessionContract.ChannelRequired)] string? Channel,
    [Required(ErrorMessage = ConcessionContract.TotalAmountRequired)] decimal? TotalAmount,
    [Required(ErrorMessage = ConcessionContract.ItemsRequired)]
    [MinLength(1, ErrorMessage = ConcessionContract.ItemsRequired)]
    [MaxLength(ConcessionContract.MaxItems, ErrorMessage = ConcessionContract.ItemsTooMany)]
    IReadOnlyList<PurchaseItemRequest?>? Items) : IPurchaseRequest;

/// <summary>Resultado da verificação. A lista <see cref="Applicable"/> vem na ordem de recomendação (D-8), o recomendado primeiro.</summary>
/// <param name="OnlyOnePerPurchase">Sempre verdadeiro: apenas um voucher por compra (US-05.1).</param>
/// <param name="RedemptionId">Resgate já feito nesta transação, se houver.</param>
public record RedemptionCheckResponse(
    string TransactionId,
    bool OnlyOnePerPurchase,
    Guid? RedemptionId,
    IReadOnlyList<ApplicableVoucherResponse> Applicable,
    IReadOnlyList<InapplicableVoucherResponse> NotApplicable);

/// <param name="DiscountAmount">Desconto previsto nesta compra (D-7).</param>
/// <param name="Recommended">O de maior desconto (RF17b).</param>
public record ApplicableVoucherResponse(VoucherResponse Voucher, decimal DiscountAmount, bool Recommended);

/// <param name="Reason">Motivo (<see cref="Domain.Vouchers.RedemptionRefusal"/>), para exibição opcional ao cliente.</param>
public record InapplicableVoucherResponse(VoucherResponse Voucher, string Reason, string Detail);

/// <summary>Resgate gravado (US-07.3 cenário 2), com o voucher já no estado posterior ao resgate. Datas no fuso da rede.</summary>
/// <param name="Status"><c>Effective</c> ou <c>Reversed</c> (D-19).</param>
public record RedemptionResponse(
    Guid RedemptionId,
    string TransactionId,
    string Status,
    VoucherResponse Voucher,
    decimal DiscountAmount,
    DateTimeOffset RedeemedAt,
    DateTimeOffset? ReversedAt);

/// <summary>Verificação de aplicáveis, resgate e estorno (EP-05, US-07.3). A rede é a da aplicação do token.</summary>
public interface IRedemptionService
{
    Task<RedemptionCheckResponse> CheckAsync(RedemptionCheckRequest request, CancellationToken cancellationToken);

    Task<RedemptionResponse> RedeemAsync(RedemptionRequest request, CancellationToken cancellationToken);

    Task<RedemptionResponse> ReverseAsync(Guid redemptionId, CancellationToken cancellationToken);

    Task<RedemptionResponse> GetAsync(Guid redemptionId, CancellationToken cancellationToken);
}

/// <summary>
/// Estorna o resgate efetivo de um voucher dentro da transação de quem chama (GH-049, D-10): o desfazimento e a
/// expiração da concessão de um provisório resgatado na própria compra devolvem a utilização.
/// </summary>
public interface IRedemptionReverser
{
    /// <summary>Marca este resgate como estornado, devolve a utilização e grava o evento; não chama <c>SaveChanges</c>.</summary>
    /// <param name="actorApplicationId">Aplicação que provocou o estorno; nula na varredura (só o evento é gravado, sem auditoria).</param>
    /// <returns>Falso quando o resgate já não estava efetivo.</returns>
    Task<bool> ReverseAsync(Guid redemptionId, string cause, Guid? actorApplicationId, DateTime utcNow, CancellationToken cancellationToken);

    /// <summary>Estorna todos os resgates efetivos do voucher (ao descartá-lo, D-10). Devolve quantos foram estornados.</summary>
    Task<int> ReverseAllOfVoucherAsync(Guid voucherId, string cause, Guid? actorApplicationId, DateTime utcNow, CancellationToken cancellationToken);
}

/// <summary>Persistência do resgate. O consumo e a devolução da utilização são comandos condicionais únicos (D-9, D-19).</summary>
public interface IRedemptionRepository
{
    /// <summary>Vouchers não descartados do cliente na rede, com campanha (rede e escopo), alvo e concessão. Sem rastreamento.</summary>
    Task<IReadOnlyList<Voucher>> GetCustomerVouchersAsync(Guid networkId, string customerDocument, CancellationToken cancellationToken);

    /// <summary>Voucher do cliente na rede, com as mesmas navegações; null para outro CPF ou outra rede (D-12).</summary>
    Task<Voucher?> FindVoucherAsync(Guid voucherId, Guid networkId, string customerDocument, CancellationToken cancellationToken);

    /// <summary>Resgate da transação na rede, com o voucher e as navegações dele.</summary>
    Task<Redemption?> FindByTransactionAsync(Guid networkId, string transactionId, CancellationToken cancellationToken);

    Task<Redemption?> FindByIdAsync(Guid id, Guid networkId, CancellationToken cancellationToken);

    /// <summary>Resgate pelo id, de qualquer rede e sem navegações (uso interno do estorno).</summary>
    Task<Redemption?> FindAsync(Guid redemptionId, CancellationToken cancellationToken);

    /// <summary>Ids dos resgates efetivos do voucher.</summary>
    Task<IReadOnlyList<Guid>> GetEffectiveIdsByVoucherAsync(Guid voucherId, CancellationToken cancellationToken);

    Task<IConcessionTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// <c>uses_count + 1</c> num disponível (vira resgatado ao chegar ao limite) ou num provisório com a reserva em dia.
    /// Primeiro comando da transação. Falso quando outro pedido consumiu antes ou o voucher mudou de estado.
    /// </summary>
    Task<bool> TryConsumeUseAsync(Guid voucherId, DateTime utcNow, CancellationToken cancellationToken);

    /// <summary><c>uses_count - 1</c>; resgatado volta a disponível. Falso quando não havia uso a devolver.</summary>
    Task<bool> TryReturnUseAsync(Guid voucherId, CancellationToken cancellationToken);

    /// <summary>Este resgate, se efetivo, passa a estornado. Falso quando já não estava efetivo.</summary>
    Task<bool> TryReverseAsync(Guid redemptionId, DateTime reversedAt, CancellationToken cancellationToken);

    /// <summary>Disponível com a validade vencida passa a expirado (US-05.3, cenário 2). Falso quando já não estava disponível.</summary>
    Task<bool> TryMarkExpiredAsync(Guid voucherId, DateTime utcNow, CancellationToken cancellationToken);

    void Add(Redemption redemption);

    void Add(VoucherEvent voucherEvent);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Falso quando a transação já tem resgate na rede (índice único): outro pedido chegou antes.</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);
}
