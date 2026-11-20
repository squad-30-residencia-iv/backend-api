using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

/// <summary>Janelas de resgate da campanha (spec GH-070, D-3 e D-7): dias como máscara de bits, horas em minutos do dia.</summary>
public class CampaignRedemptionWindowConfiguration : IEntityTypeConfiguration<CampaignRedemptionWindow>
{
    public void Configure(EntityTypeBuilder<CampaignRedemptionWindow> builder)
    {
        builder.ToTable("campaign_redemption_windows", table =>
        {
            table.HasCheckConstraint("ck_campaign_redemption_windows_days", "days_of_week > 0 AND days_of_week < 128");
            table.HasCheckConstraint(
                "ck_campaign_redemption_windows_minutes",
                "start_minute >= 0 AND start_minute < 1440 AND end_minute >= 0 AND end_minute < 1440");
        });

        builder.HasKey(window => window.Id);
        builder.Property(window => window.Id).ValueGeneratedNever();

        builder.Ignore(window => window.Days);
        builder.Ignore(window => window.Overnight);

        builder.HasOne(window => window.Campaign)
            .WithMany(campaign => campaign.RedemptionWindows)
            .HasForeignKey(window => window.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
