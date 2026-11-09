namespace VoucherCampaigns.Domain.Audit;

/// <summary>Quem fez a ação auditada. Gravado como texto.</summary>
public enum AuditActorType
{
    /// <summary>Usuário do portal (analista, gestor ou administrador); o id é o <c>sub</c> do token.</summary>
    User,

    /// <summary>Aplicação satélite (US-07.5); o id é o da aplicação em <c>satellite_apps</c>.</summary>
    Application
}
