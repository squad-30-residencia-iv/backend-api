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

    /// <summary>
    /// Duas gravações simultâneas da mesma campanha (spec GH-047, 7.1 e PA-10): edição do rascunho ou mudança de
    /// status. A segunda é recusada para não somar listas nem aprovar duas vezes.
    /// </summary>
    public const string ConcurrentUpdateCode = "CAMPAIGN_CONCURRENT_UPDATE";
    public const string ConcurrentUpdateMessage = "A campanha foi alterada por outra requisição ao mesmo tempo. Recarregue e tente de novo.";

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

    public const string VoucherValidUntilBeforeEndCode = "CAMPAIGN_VOUCHER_VALID_UNTIL_BEFORE_END";
    public const string VoucherValidUntilBeforeEndMessage = "A validade fixa do voucher não pode ser anterior ao fim da campanha";

    public const string VoucherValidityCapBeforeStartCode = "CAMPAIGN_VOUCHER_VALIDITY_CAP_BEFORE_START";
    public const string VoucherValidityCapBeforeStartMessage = "O teto da validade do voucher deve ser posterior ao início da campanha";

    public const string PurchaseRangeInvalidCode = "CAMPAIGN_PURCHASE_RANGE_INVALID";
    public const string PurchaseRangeInvalidMessage = "O valor mínimo da compra não pode ser maior que o valor máximo";

    /// <summary>Mensagem exata da US-01.4, cenário 3.</summary>
    public const string ConditionMinQuantityNotPositiveCode = "CAMPAIGN_CONDITION_MIN_QUANTITY_NOT_POSITIVE";
    public const string ConditionMinQuantityNotPositiveMessage = "A quantidade mínima de itens deve ser maior que zero";

    public const string ConditionMinPurchasesNotPositiveCode = "CAMPAIGN_CONDITION_MIN_PURCHASES_NOT_POSITIVE";
    public const string ConditionMinPurchasesNotPositiveMessage = "A quantidade mínima de compras deve ser maior que zero";

    public const string ConditionMinAmountNotPositiveCode = "CAMPAIGN_CONDITION_MIN_AMOUNT_NOT_POSITIVE";
    public const string ConditionMinAmountNotPositiveMessage = "O valor mínimo da compra deve ser maior que zero";

    public const string ConditionTypeUnavailableCode = "CAMPAIGN_CONDITION_TYPE_UNAVAILABLE";
    public const string ConditionTypeUnavailableMessage = "Este tipo de condição ainda não está disponível.";

    public const string ConditionDuplicatedCode = "CAMPAIGN_CONDITION_DUPLICATED";
    public const string ConditionDuplicatedMessage = "A mesma condição foi informada mais de uma vez.";

    public const string ConditionCategoryNotFoundCode = "CAMPAIGN_CONDITION_CATEGORY_NOT_FOUND";
    public const string ConditionCategoryInactiveCode = "CAMPAIGN_CONDITION_CATEGORY_INACTIVE";
    public const string ConditionProductNotFoundCode = "CAMPAIGN_CONDITION_PRODUCT_NOT_FOUND";
    public const string ConditionProductInactiveCode = "CAMPAIGN_CONDITION_PRODUCT_INACTIVE";

    public const string SubmitNotDraftCode = "CAMPAIGN_SUBMIT_NOT_DRAFT";
    public const string SubmitNotDraftMessage = "Somente um rascunho pode ser submetido para aprovação.";

    public const string SubmitAudienceRequiredCode = "CAMPAIGN_SUBMIT_AUDIENCE_REQUIRED";
    public const string SubmitAudienceRequiredMessage = "Defina o público antes de submeter a campanha.";

    public const string SubmitItemsRequiredCode = "CAMPAIGN_SUBMIT_ITEMS_REQUIRED";
    public const string SubmitItemsRequiredMessage = "Informe ao menos um alvo antes de submeter a campanha.";

    public const string SubmitTotalRequiredCode = "CAMPAIGN_SUBMIT_TOTAL_REQUIRED";
    public const string SubmitTotalRequiredMessage = "Informe a quantidade total de vouchers antes de submeter a campanha.";

    public const string SubmitValidityRequiredCode = "CAMPAIGN_SUBMIT_VALIDITY_REQUIRED";
    public const string SubmitValidityRequiredMessage = "Defina a validade do voucher antes de submeter a campanha.";

    public const string SubmitMaxUsesRequiredCode = "CAMPAIGN_SUBMIT_MAX_USES_REQUIRED";
    public const string SubmitMaxUsesRequiredMessage = "Informe as utilizações por voucher antes de submeter a campanha.";

    public const string DecisionNotPendingCode = "CAMPAIGN_DECISION_NOT_PENDING";
    public const string DecisionNotPendingMessage = "Somente uma campanha em aprovação pode ser aprovada ou reprovada.";

    public const string ApprovePeriodEndedCode = "CAMPAIGN_APPROVE_PERIOD_ENDED";
    public const string ApprovePeriodEndedMessage = "A vigência da campanha já terminou.";

    public const string SubmitScopeRequiredCode = "CAMPAIGN_SUBMIT_SCOPE_REQUIRED";
    public const string SubmitScopeRequiredMessage = "Defina onde a campanha vale (rede, região ou loja e canais) antes de submetê-la.";

    public const string ScopeDuplicatedCode = "CAMPAIGN_SCOPE_DUPLICATED";
    public const string ScopeDuplicatedMessage = "O mesmo local foi informado mais de uma vez com o mesmo canal.";

    public const string ScopeRegionNotFoundCode = "CAMPAIGN_SCOPE_REGION_NOT_FOUND";
    public const string ScopeRegionInactiveCode = "CAMPAIGN_SCOPE_REGION_INACTIVE";

    public const string ScopeEstablishmentNotFoundCode = "CAMPAIGN_SCOPE_ESTABLISHMENT_NOT_FOUND";
    public const string ScopeEstablishmentNotFoundMessage = "Loja informada não encontrada";

    public const string ScopeEstablishmentInactiveCode = "CAMPAIGN_SCOPE_ESTABLISHMENT_INACTIVE";
    public const string ScopeEstablishmentInactiveMessage = "Loja informada está inativa";

    public const string ScopeOutsideNetworkCode = "CAMPAIGN_SCOPE_OUTSIDE_NETWORK";
    public const string ScopeOutsideNetworkMessage = "O local informado não pertence à rede da campanha.";

    /// <summary>PR-11: o canal é atributo da loja. Parâmetros: código da loja e nome do canal.</summary>
    public const string ScopeChannelNotEnabledCode = "CAMPAIGN_SCOPE_CHANNEL_NOT_ENABLED";
    public const string ScopeChannelNotEnabledMessage = "A loja {0} não opera o canal {1}.";

    public const string PauseNotActiveCode = "CAMPAIGN_PAUSE_NOT_ACTIVE";
    public const string PauseNotActiveMessage = "Somente uma campanha ativa pode ser pausada.";

    public const string EndNotAllowedCode = "CAMPAIGN_END_NOT_ALLOWED";
    public const string EndNotAllowedMessage = "Somente uma campanha ativa ou pausada pode ser encerrada.";

    /// <summary>Spec GH-061, D-2: cancela pelo status efetivo; encerrada fica de fora (PA-2).</summary>
    public const string CancelNotAllowedCode = "CAMPAIGN_CANCEL_NOT_ALLOWED";
    public const string CancelNotAllowedMessage = "Somente uma campanha agendada, ativa, pausada ou esgotada pode ser cancelada.";
}
