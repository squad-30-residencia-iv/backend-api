using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Audit;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        // Sem FK: o registro sobrevive à campanha e também vai apontar para voucher (spec GH-026-trilha-de-auditoria, D-5).
        builder.ToTable("audit_entries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.EventType).HasMaxLength(50).IsRequired();
        builder.Property(entry => entry.ActorType).HasConversion<string>().HasMaxLength(20);
        builder.Property(entry => entry.EntityType).HasMaxLength(50).IsRequired();
        builder.Property(entry => entry.Payload).HasMaxLength(2000);

        builder.HasIndex(entry => new { entry.CampaignId, entry.OccurredAt });
        builder.HasIndex(entry => new { entry.NetworkId, entry.OccurredAt });
    }
}
