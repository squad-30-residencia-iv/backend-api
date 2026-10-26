namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Mensagens de contrato e de recurso ausente das rotas de campanha.</summary>
public static class CampaignContract
{
    public const string NetworkRequired = "Informe a rede da campanha.";
    public const string StatusInvalid =
        "Status inválido. Use Draft, PendingApproval, Scheduled, Active, Paused, Ended, Cancelled ou SoldOut.";

    public const string CampaignNotFound = "Campanha não encontrada.";
}
