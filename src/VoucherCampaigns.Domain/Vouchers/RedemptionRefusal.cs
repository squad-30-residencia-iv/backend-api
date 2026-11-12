namespace VoucherCampaigns.Domain.Vouchers;

/// <summary>Por que um voucher do cliente não serve nesta compra (spec GH-049, D-6), na ordem de avaliação do fluxo 1.2.</summary>
public enum RedemptionRefusal
{
    /// <summary>Provisório de outra compra: só vale depois da confirmação dela.</summary>
    NotConfirmed,

    /// <summary>Provisório desta compra, mas a campanha não permite uso na mesma compra (US-04.9, cenário 5).</summary>
    SamePurchaseNotAllowed,

    /// <summary>Validade própria vencida (US-05.3).</summary>
    Expired,

    /// <summary>Todas as utilizações consumidas (US-05.4).</summary>
    NoUsesLeft,

    /// <summary>Voucher cancelado (RN10).</summary>
    Cancelled,

    /// <summary>Campanha de origem cancelada (RN10, US-05.5 cenário 2).</summary>
    CampaignCancelled,

    /// <summary>Loja ou canal fora do escopo da campanha (RN08, US-05.2 cenário 4).</summary>
    OutOfScope,

    /// <summary>Valor bruto abaixo do mínimo da campanha (US-05.2 cenário 2).</summary>
    AmountBelowMinimum,

    /// <summary>Valor bruto acima do máximo da campanha (US-05.2 cenário 2).</summary>
    AmountAboveMaximum,

    /// <summary>Nenhum item da compra casa o alvo do voucher (US-05.2 cenário 3).</summary>
    TargetNotInPurchase,

    /// <summary>Esta compra já usou outro voucher (US-07.3 cenário 4).</summary>
    TransactionAlreadyRedeemed,

    /// <summary>Este voucher já foi resgatado nesta compra; repetir o resgate devolve o original.</summary>
    AlreadyRedeemedInThisPurchase
}
