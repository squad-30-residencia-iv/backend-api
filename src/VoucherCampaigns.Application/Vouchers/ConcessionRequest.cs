using System.ComponentModel.DataAnnotations;

namespace VoucherCampaigns.Application.Vouchers;

/// <summary>Compra com o carrinho fechado, enviada pela aplicação satélite para avaliação de concessão (US-07.2).</summary>
/// <param name="TransactionId">Identificador único da transação no satélite: chave de idempotência (PR-07).</param>
/// <param name="Customer">Cliente identificado pelo CPF (D4).</param>
/// <param name="EstablishmentId">Loja da compra, da rede da aplicação.</param>
/// <param name="Channel"><c>Physical</c> ou <c>Virtual</c>.</param>
/// <param name="TotalAmount">Valor bruto da compra (PR-16), maior que zero e com até 2 casas.</param>
/// <param name="Items">Itens da compra, de 1 a 500.</param>
/// <param name="CustomerPurchaseCount">Quantidade de compras do cliente, para a condição correspondente (D2).</param>
public record ConcessionRequest(
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
    IReadOnlyList<PurchaseItemRequest?>? Items,
    [Range(0, int.MaxValue, ErrorMessage = ConcessionContract.CustomerPurchaseCountRange)] int? CustomerPurchaseCount) : IPurchaseRequest;

public record ConcessionCustomerRequest(
    [Required(ErrorMessage = ConcessionContract.CustomerDocumentRequired)] string? Document);

/// <param name="ProductCode">Código do produto no catálogo (<c>products.code</c>); desconhecido conta só no total e na quantidade.</param>
public record PurchaseItemRequest(
    [Required(ErrorMessage = ConcessionContract.ItemProductCodeRequired)]
    [StringLength(ConcessionContract.MaxProductCodeLength, ErrorMessage = ConcessionContract.ItemProductCodeTooLong)]
    string? ProductCode,
    [Required(ErrorMessage = ConcessionContract.ItemQuantityRange)]
    [Range(1, ConcessionContract.MaxItemQuantity, ErrorMessage = ConcessionContract.ItemQuantityRange)]
    int? Quantity,
    [Required(ErrorMessage = ConcessionContract.ItemUnitPriceRequired)] decimal? UnitPrice);
