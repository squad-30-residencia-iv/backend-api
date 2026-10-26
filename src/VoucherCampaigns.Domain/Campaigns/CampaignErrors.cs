namespace VoucherCampaigns.Domain.Campaigns;

/// <summary>Códigos e mensagens das regras de campanha (mensagens exatas do backlog quando definidas).</summary>
public static class CampaignErrors
{
    public const string InvalidPeriodCode = "CAMPAIGN_INVALID_PERIOD";
    public const string InvalidPeriodMessage = "A data de início não pode ser posterior à data de fim";

    public const string StartInPastCode = "CAMPAIGN_START_IN_PAST";
    public const string StartInPastMessage = "A data de início não pode ser anterior à data atual";

    public const string EndInPastCode = "CAMPAIGN_END_IN_PAST";
    public const string EndInPastMessage = "A data de fim não pode ser anterior à data atual";

    public const string NetworkUnavailableCode = "NETWORK_UNAVAILABLE";
    public const string NetworkUnavailableMessage = "A rede informada não existe ou está inativa.";

    public const string NotDraftCode = "CAMPAIGN_NOT_DRAFT";
    public const string NotDraftMessage = "Somente campanhas em rascunho podem ter a configuração alterada.";

    public const string BenefitNotPositiveCode = "CAMPAIGN_BENEFIT_NOT_POSITIVE";
    public const string BenefitNotPositiveMessage = "O valor do benefício deve ser maior que zero.";

    public const string BenefitPercentageAbove100Code = "CAMPAIGN_BENEFIT_PERCENTAGE_ABOVE_100";
    public const string BenefitPercentageAbove100Message = "O benefício percentual não pode ultrapassar 100%.";

    public const string DuplicatedTargetCode = "CAMPAIGN_ITEM_DUPLICATED_TARGET";
    public const string DuplicatedTargetMessage = "O mesmo alvo foi informado mais de uma vez.";

    public const string ItemCategoryNotFoundCode = "CAMPAIGN_ITEM_CATEGORY_NOT_FOUND";
    public const string ItemCategoryInactiveCode = "CAMPAIGN_ITEM_CATEGORY_INACTIVE";
    public const string ItemProductNotFoundCode = "CAMPAIGN_ITEM_PRODUCT_NOT_FOUND";
    public const string ItemProductNotFoundMessage = "Produto informado não encontrado";
    public const string ItemProductInactiveCode = "CAMPAIGN_ITEM_PRODUCT_INACTIVE";
    public const string ItemProductInactiveMessage = "Produto informado está inativo";

    public const string QuotaSingleTargetCode = "CAMPAIGN_ITEM_QUOTA_SINGLE_TARGET";
    public const string QuotaSingleTargetMessage = "A quantidade por alvo só se aplica quando a campanha tem mais de um alvo.";

    /// <summary>RN03a, mensagem exata da US-01.3, cenário 3.</summary>
    public const string QuotasExceedTotalCode = "CAMPAIGN_ITEM_QUOTAS_EXCEED_TOTAL";
    public const string QuotasExceedTotalMessage =
        "A soma das quantidades por alvo ({0}) ultrapassa a quantidade total da campanha ({1})";

    public const string AudienceRestrictedUnavailableCode = "CAMPAIGN_AUDIENCE_RESTRICTED_UNAVAILABLE";
    public const string AudienceRestrictedUnavailableMessage =
        "O público restrito ainda não está disponível: os critérios de elegibilidade serão configurados no EP-06.";
}
