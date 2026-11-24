using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class CampaignCustomerGroupConfiguration : IEntityTypeConfiguration<CampaignCustomerGroup>
{
    public void Configure(EntityTypeBuilder<CampaignCustomerGroup> builder)
    {
        builder.ToTable("campaign_customer_groups");
        builder.HasKey(link => link.Id);
        builder.Property(link => link.Id).ValueGeneratedNever();
        builder.HasIndex(link => new { link.CampaignId, link.GroupId }).IsUnique();
        builder.HasOne(link => link.Campaign)
            .WithMany(campaign => campaign.CustomerGroups)
            .HasForeignKey(link => link.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(link => link.Group)
            .WithMany()
            .HasForeignKey(link => link.GroupId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
