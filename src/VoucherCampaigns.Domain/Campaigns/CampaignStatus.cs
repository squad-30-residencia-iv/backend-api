namespace VoucherCampaigns.Domain.Campaigns;

/// <summary>Status da campanha (PR-02). Gravado como texto; a ordem dos valores não é persistida.</summary>
public enum CampaignStatus
{
    /// <summary>Rascunho: em configuração, não gera vouchers (PR-01).</summary>
    Draft,

    /// <summary>Em aprovação: submetida pelo analista, aguardando o gestor (US-01.6, US-01.11).</summary>
    PendingApproval,

    /// <summary>Agendada: aprovada, com vigência futura (PR-04).</summary>
    Scheduled,

    /// <summary>Ativa: em vigor; único status que concede vouchers (PR-10).</summary>
    Active,

    /// <summary>Pausada.</summary>
    Paused,

    /// <summary>Encerrada.</summary>
    Ended,

    /// <summary>Cancelada.</summary>
    Cancelled,

    /// <summary>Esgotada.</summary>
    SoldOut
}
