namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Mensagens de contrato e de recurso ausente das rotas de campanha.</summary>
public static class CampaignContract
{
    public const int MaxItems = 100;

    /// <summary>Maior valor de <c>decimal(10,2)</c>.</summary>
    public const decimal MaxBenefitValue = 99_999_999.99m;

    public const string NetworkRequired = "Informe a rede da campanha.";
    public const string NameRequired = "Informe o nome da campanha.";
    public const string NameTooLong = "O nome deve ter no máximo 150 caracteres.";
    public const string DescriptionTooLong = "A descrição deve ter no máximo 500 caracteres.";
    public const string StartsAtRequired = "Informe a data e hora de início.";
    public const string EndsAtRequired = "Informe a data e hora de fim.";
    public const string StatusInvalid =
        "Status inválido. Use Draft, PendingApproval, Scheduled, Active, Paused, Ended, Cancelled ou SoldOut.";

    public const string ItemsRequired = "Informe ao menos um alvo da campanha.";
    public const string ItemsTooMany = "A campanha pode ter no máximo 100 alvos.";
    public const string TargetTypeRequired = "Informe o tipo do alvo.";
    public const string TargetTypeInvalid = "Tipo de alvo inválido. Use All, Category ou Product.";
    public const string TargetCategoryRequired = "Informe a categoria do alvo, sem produto.";
    public const string TargetProductRequired = "Informe o produto do alvo, sem categoria.";
    public const string TargetAllWithId = "O alvo All não aceita categoria nem produto.";
    public const string BenefitTypeRequired = "Informe o tipo do benefício.";
    public const string BenefitTypeInvalid = "Tipo de benefício inválido. Use Percentage ou FixedAmount.";
    public const string BenefitValueRequired = "Informe o valor do benefício.";
    public const string BenefitValueScale = "O valor do benefício deve ter no máximo 2 casas decimais.";
    public const string BenefitValueTooLarge = "O valor do benefício deve ser no máximo 99.999.999,99.";

    public const string ItemVoucherLimitPositive = "A quantidade por alvo deve ser maior que zero.";
    public const string TotalVoucherLimitRequired = "Informe a quantidade total de vouchers.";
    public const string TotalVoucherLimitPositive = "A quantidade total de vouchers deve ser maior que zero.";
    public const string PerCustomerVoucherLimitPositive = "O limite de vouchers por cliente deve ser maior que zero.";

    public const string AudienceTypeRequired = "Informe o público da campanha.";
    public const string AudienceTypeInvalid = "Público inválido. Use AllCustomers ou Restricted.";

    public const int MaxValidityDays = 3_650;
    public const int MaxValidityHours = 87_600;

    public const string ValidityTypeRequired = "Informe o tipo de validade do voucher.";
    public const string ValidityTypeInvalid = "Tipo de validade inválido. Use Relative ou FixedDate.";
    public const string ValidityAmountRequired = "Informe o prazo de validade do voucher.";
    public const string ValidityAmountRange = "O prazo de validade deve ser de 1 a 3.650 dias ou de 1 a 87.600 horas.";
    public const string ValidityUnitRequired = "Informe a unidade do prazo de validade.";
    public const string ValidityUnitInvalid = "Unidade inválida. Use Days ou Hours.";
    public const string ValidUntilRequired = "Informe a data de validade do voucher.";
    public const string ValidityRelativeWithDate = "A validade relativa não aceita data fixa.";
    public const string ValidityFixedWithRelative = "A validade por data fixa não aceita prazo, unidade nem teto.";

    public const string PurchaseAmountPositive = "O valor da compra deve ser maior que zero.";
    public const string PurchaseAmountScale = "O valor da compra deve ter no máximo 2 casas decimais.";
    public const string PurchaseAmountTooLarge = "O valor da compra deve ser no máximo 99.999.999,99.";
    public const string MaxUsesRequired = "Informe a quantidade de utilizações por voucher.";
    public const string MaxUsesRange = "A quantidade de utilizações por voucher deve ser de 1 a 100.";

    public const int MaxConditions = 20;

    public const string ConditionsRequired = "Informe a lista de condições (pode ser vazia).";
    public const string ConditionsTooMany = "A campanha pode ter no máximo 20 condições.";
    public const string AllowUseInSamePurchaseRequired = "Informe se o voucher pode ser usado na mesma compra que o gerou.";
    public const string ConditionTypeRequired = "Informe o tipo da condição.";
    public const string ConditionTypeInvalid =
        "Tipo de condição inválido. Use PurchaseTotal, ItemQuantity, Category, Product, CustomerPurchaseCount, ProductCombination ou PurchasePeriod.";
    public const string ConditionMinAmountRequired = "Informe o valor mínimo da compra.";
    public const string ConditionMinAmountScale = "O valor mínimo da compra deve ter no máximo 2 casas decimais.";
    public const string ConditionMinAmountTooLarge = "O valor mínimo da compra deve ser no máximo 99.999.999,99.";
    public const string ConditionMinQuantityRequired = "Informe a quantidade mínima de itens.";
    public const string ConditionCategoryRequired = "Informe a categoria da condição.";
    public const string ConditionProductRequired = "Informe o produto da condição.";
    public const string ConditionUnexpectedField = "Este campo não se aplica ao tipo de condição informado.";

    public const string CampaignNotFound = "Campanha não encontrada.";
}
