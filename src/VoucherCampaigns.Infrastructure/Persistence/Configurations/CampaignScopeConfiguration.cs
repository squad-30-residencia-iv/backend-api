using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class CampaignScopeConfiguration : IEntityTypeConfiguration<CampaignScope>
{
    public void Configure(EntityTypeBuilder<CampaignScope> builder)
    {
        builder.ToTable("campaign_scopes", table =>
            table.HasCheckConstraint(
                "ck_campaign_scopes_single_node",
                "region_id IS NULL OR establishment_id IS NULL"));

        builder.HasKey(scope => scope.Id);

        builder.Property(scope => scope.ChannelType).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(scope => scope.Campaign)
            .WithMany(campaign => campaign.Scopes)
            .HasForeignKey(scope => scope.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(scope => scope.Region)
            .WithMany()
            .HasForeignKey(scope => scope.RegionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(scope => scope.Establishment)
            .WithMany()
            .HasForeignKey(scope => scope.EstablishmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
