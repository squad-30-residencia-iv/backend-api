namespace VoucherCampaigns.Application.Vouchers;

/// <summary>Mensagens de contrato e de recurso ausente das rotas de concessão (spec GH-045, 2.3).</summary>
public static class ConcessionContract
{
    public const int MaxTransactionIdLength = 100;
    public const int MaxProductCodeLength = 50;
    public const int MaxItems = 500;
    public const int MaxItemQuantity = 100_000;
    public const int DocumentLength = 11;

    /// <summary>Maior valor de <c>decimal(10,2)</c>.</summary>
    public const decimal MaxAmount = 99_999_999.99m;

    public const string TransactionIdRequired = "Informe o identificador da transação.";
    public const string TransactionIdTooLong = "O identificador da transação deve ter no máximo 100 caracteres.";
    public const string CustomerRequired = "Informe o cliente da compra.";
    public const string CustomerDocumentRequired = "Informe o CPF do cliente.";
    public const string CustomerDocumentInvalid = "O CPF deve ter 11 dígitos.";
    public const string EstablishmentRequired = "Informe a loja da compra.";
    public const string ChannelRequired = "Informe o canal da compra.";
    public const string ChannelInvalid = "Canal inválido. Use Physical ou Virtual.";
    public const string TotalAmountRequired = "Informe o valor total da compra.";
    public const string TotalAmountPositive = "O valor total da compra deve ser maior que zero.";
    public const string TotalAmountScale = "O valor total da compra deve ter no máximo 2 casas decimais.";
    public const string TotalAmountTooLarge = "O valor total da compra deve ser no máximo 99.999.999,99.";
    public const string ItemsRequired = "Informe os itens da compra.";
    public const string ItemsTooMany = "A compra pode ter no máximo 500 itens.";
    public const string ItemProductCodeRequired = "Informe o código do produto.";
    public const string ItemProductCodeTooLong = "O código do produto deve ter no máximo 50 caracteres.";
    public const string ItemQuantityRange = "A quantidade do item deve ser de 1 a 100.000.";
    public const string ItemUnitPriceRequired = "Informe o preço unitário do item.";
    public const string ItemUnitPriceInvalid = "O preço unitário não pode ser negativo e deve ter no máximo 2 casas decimais.";
    public const string CustomerPurchaseCountRange = "A quantidade de compras do cliente não pode ser negativa.";
    public const string VoucherStatusInvalid = "Status inválido. Use Provisional, Available, Redeemed, Expired ou Cancelled.";

    public const string ConcessionNotFound = "Concessão não encontrada.";

    /// <summary>Filtro <c>establishmentId</c> da consulta de campanhas vigentes (US-07.1): loja inexistente ou de outra rede.</summary>
    public const string EstablishmentNotFoundForList = "A loja informada não existe nesta rede.";
}
