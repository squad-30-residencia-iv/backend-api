using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class CampaignStatusEventConfiguration : IEntityTypeConfiguration<CampaignStatusEvent>
{
    public void Configure(EntityTypeBuilder<CampaignStatusEvent> builder)
    {
        builder.ToTable("campaign_status_events");
        builder.HasKey(statusEvent => statusEvent.Id);
        builder.Property(statusEvent => statusEvent.Id).ValueGeneratedNever();
        builder.Property(statusEvent => statusEvent.FromStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(statusEvent => statusEvent.ToStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(statusEvent => statusEvent.Reason).HasMaxLength(500);

        builder.HasIndex(statusEvent => statusEvent.CampaignId);

        builder.HasOne(statusEvent => statusEvent.Campaign)
            .WithMany(campaign => campaign.StatusEvents)
            .HasForeignKey(statusEvent => statusEvent.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
