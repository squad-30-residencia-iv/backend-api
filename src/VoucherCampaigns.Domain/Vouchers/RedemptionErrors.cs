namespace VoucherCampaigns.Domain.Vouchers;

/// <summary>Códigos e mensagens das rotas de verificação e resgate (spec GH-049, 2.3).</summary>
public static class RedemptionErrors
{
    public const string EstablishmentNotFoundCode = "REDEMPTION_ESTABLISHMENT_NOT_FOUND";
    public const string EstablishmentNotFoundMessage = "A loja informada não existe nesta rede.";

    public const string EstablishmentInactiveCode = "REDEMPTION_ESTABLISHMENT_INACTIVE";
    public const string EstablishmentInactiveMessage = "A loja informada está inativa; o resgate não é avaliado até a loja voltar.";

    public const string ChannelNotEnabledCode = "REDEMPTION_CHANNEL_NOT_ENABLED";
    public const string ChannelNotEnabledMessage = "A loja {0} não opera o canal {1}.";

    public const string VoucherNotFoundMessage = "Voucher não encontrado.";
    public const string RedemptionNotFoundMessage = "Resgate não encontrado.";

    /// <summary>Código do <c>422</c> de um motivo: <c>REDEMPTION_</c> + motivo em SNAKE_CASE (D-6).</summary>
    public static string CodeOf(RedemptionRefusal refusal) =>
        "REDEMPTION_" + string.Concat(refusal.ToString().Select((character, index) =>
            index > 0 && char.IsUpper(character) ? "_" + character : character.ToString())).ToUpperInvariant();

    /// <summary>Mensagem do motivo; <c>{0}</c> é a validade (Expired), o mínimo ou o máximo (Amount*), formatados pelo chamador.</summary>
    public static string MessageOf(RedemptionRefusal refusal) => refusal switch
    {
        RedemptionRefusal.NotConfirmed => "O voucher é provisório de outra compra; só vale depois da confirmação dela.",
        RedemptionRefusal.SamePurchaseNotAllowed => "A campanha não permite usar o voucher na compra que o gerou.",
        RedemptionRefusal.Expired => "O voucher venceu em {0}.",
        RedemptionRefusal.NoUsesLeft => "O voucher já consumiu todas as utilizações.",
        RedemptionRefusal.Cancelled => "O voucher foi cancelado.",
        RedemptionRefusal.CampaignCancelled => "A campanha do voucher foi cancelada.",
        RedemptionRefusal.OutOfScope => "O voucher não vale nesta loja ou canal.",
        RedemptionRefusal.AmountBelowMinimum => "A compra precisa ser de pelo menos R$ {0} para usar este voucher.",
        RedemptionRefusal.AmountAboveMaximum => "A compra não pode passar de R$ {0} para usar este voucher.",
        RedemptionRefusal.TargetNotInPurchase => "A compra não tem itens do alvo deste voucher.",
        RedemptionRefusal.TransactionAlreadyRedeemed => "Esta compra já usou um voucher; só um voucher por compra.",
        RedemptionRefusal.AlreadyRedeemedInThisPurchase => "Este voucher já foi resgatado nesta compra.",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, null)
    };
}
