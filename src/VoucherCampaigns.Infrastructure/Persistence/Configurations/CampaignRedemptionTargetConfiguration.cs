using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

/// <summary>Alvos de resgate da campanha (spec GH-070, D-2 e D-7).</summary>
public class CampaignRedemptionTargetConfiguration : IEntityTypeConfiguration<CampaignRedemptionTarget>
{
    public void Configure(EntityTypeBuilder<CampaignRedemptionTarget> builder)
    {
        builder.ToTable("campaign_redemption_targets", table =>
            table.HasCheckConstraint(
                "ck_campaign_redemption_targets_target",
                "(target_type = 'Category' AND category_id IS NOT NULL AND product_id IS NULL)"
                + " OR (target_type = 'Product' AND product_id IS NOT NULL AND category_id IS NULL)"));

        builder.HasKey(target => target.Id);

        // Ids gerados pelo serviço, em sequência, como nos alvos da concessão: a substituição grava linhas novas.
        builder.Property(target => target.Id).ValueGeneratedNever();

        builder.Property(target => target.TargetType).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(target => target.Campaign)
            .WithMany(campaign => campaign.RedemptionTargets)
            .HasForeignKey(target => target.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(target => target.Category)
            .WithMany()
            .HasForeignKey(target => target.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(target => target.Product)
            .WithMany()
            .HasForeignKey(target => target.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
