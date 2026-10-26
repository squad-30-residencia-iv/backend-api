namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Mensagens de contrato e de recurso ausente das rotas de campanha.</summary>
public static class CampaignContract
{
    public const int MaxItems = 100;

    /// <summary>Maior valor de <c>decimal(10,2)</c>.</summary>
    public const decimal MaxBenefitValue = 99_999_999.99m;

    public const string NetworkRequired = "Informe a rede da campanha.";
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

    public const string CampaignNotFound = "Campanha não encontrada.";
}
