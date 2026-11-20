namespace VoucherCampaigns.Domain.Vouchers;

/// <summary>Códigos e mensagens das regras de concessão (spec GH-045).</summary>
public static class ConcessionErrors
{
    public const string EstablishmentNotFoundCode = "ISSUANCE_ESTABLISHMENT_NOT_FOUND";
    public const string EstablishmentNotFoundMessage = "A loja informada não existe nesta rede.";

    public const string EstablishmentInactiveCode = "ISSUANCE_ESTABLISHMENT_INACTIVE";
    public const string EstablishmentInactiveMessage = "A loja informada está inativa; a concessão não é avaliada até a loja voltar.";

    public const string ChannelNotEnabledCode = "ISSUANCE_CHANNEL_NOT_ENABLED";
    public const string ChannelNotEnabledMessage = "A loja {0} não opera o canal {1}.";

    public const string NotGrantedCode = "CONCESSION_NOT_GRANTED";
    public const string NotGrantedMessage = "Esta concessão não gerou voucher.";

    public const string ReservationExpiredCode = "CONCESSION_RESERVATION_EXPIRED";
    public const string ReservationExpiredMessage = "O prazo de reserva desta concessão expirou e o voucher foi descartado.";

    /// <summary>Spec GH-061, D-6 e D-11: o provisório foi descartado pelo cancelamento da campanha.</summary>
    public const string CampaignCancelledCode = "CONCESSION_CAMPAIGN_CANCELLED";
    public const string CampaignCancelledMessage = "A campanha foi cancelada; o voucher desta compra não vale.";

    public const string AlreadyCancelledCode = "CONCESSION_ALREADY_CANCELLED";
    public const string AlreadyCancelledMessage = "Esta concessão já foi desfeita.";

    public const string AlreadyConfirmedCode = "CONCESSION_ALREADY_CONFIRMED";
    public const string AlreadyConfirmedMessage = "Esta concessão já foi confirmada.";
}
