using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        // Os CHECKs usam os nomes snake_case das colunas e valem para SQLite e PostgreSQL.
        builder.ToTable("campaigns", table =>
        {
            table.HasCheckConstraint("ck_campaigns_period", "ends_at > starts_at");
            table.HasCheckConstraint("ck_campaigns_total_voucher_limit", "total_voucher_limit IS NULL OR total_voucher_limit > 0");
            table.HasCheckConstraint(
                "ck_campaigns_per_customer_voucher_limit", "per_customer_voucher_limit IS NULL OR per_customer_voucher_limit > 0");
            table.HasCheckConstraint("ck_campaigns_voucher_validity_days", "voucher_validity_days IS NULL OR voucher_validity_days > 0");
        });

        builder.HasKey(campaign => campaign.Id);

        builder.Property(campaign => campaign.Name).HasMaxLength(150).IsRequired();
        builder.Property(campaign => campaign.Description).HasMaxLength(500);
        builder.Property(campaign => campaign.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(campaign => campaign.AudienceType).HasConversion<string>().HasMaxLength(20);
        builder.Property(campaign => campaign.VoucherValidityType).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(campaign => campaign.Status);

        builder.HasOne(campaign => campaign.Network)
            .WithMany()
            .HasForeignKey(campaign => campaign.NetworkId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
