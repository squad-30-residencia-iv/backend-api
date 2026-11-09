using VoucherCampaigns.Application.Audit;
using VoucherCampaigns.Domain.Audit;

namespace VoucherCampaigns.Infrastructure.Persistence.Repositories;

/// <summary>Inclui no contexto da requisição; o <c>SaveChanges</c> de quem chamou grava junto com a operação.</summary>
public class AuditTrail(AppDbContext context) : IAuditTrail
{
    public void Append(AuditEntry entry) => context.AuditEntries.Add(entry);
}
