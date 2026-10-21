namespace VoucherCampaigns.Domain.Campaigns;

/// <summary>Códigos e mensagens das regras de campanha (mensagens exatas do backlog quando definidas).</summary>
public static class CampaignErrors
{
    public const string InvalidPeriodCode = "CAMPAIGN_INVALID_PERIOD";
    public const string InvalidPeriodMessage = "A data de início não pode ser posterior à data de fim";

    public const string StartInPastCode = "CAMPAIGN_START_IN_PAST";
    public const string StartInPastMessage = "A data de início não pode ser anterior à data atual";

    public const string NetworkUnavailableCode = "NETWORK_UNAVAILABLE";
    public const string NetworkUnavailableMessage = "A rede informada não existe ou está inativa.";
}
