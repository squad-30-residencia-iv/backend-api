using VoucherCampaigns.Domain.Audit;

namespace VoucherCampaigns.Application.Audit;

/// <summary>
/// Trilha de auditoria append-only (spec GH-026-trilha-de-auditoria). Só inclui: o registro é gravado
/// no <c>SaveChanges</c> de quem chamou, junto com a operação; se ela falhar, nada fica gravado.
/// </summary>
public interface IAuditTrail
{
    void Append(AuditEntry entry);
}
