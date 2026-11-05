using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class CampaignItemConfiguration : IEntityTypeConfiguration<CampaignItem>
{
    public void Configure(EntityTypeBuilder<CampaignItem> builder)
    {
        builder.ToTable("campaign_items", table =>
        {
            table.HasCheckConstraint(
                "ck_campaign_items_target",
                "(target_type = 'All' AND category_id IS NULL AND product_id IS NULL)"
                + " OR (target_type = 'Category' AND category_id IS NOT NULL AND product_id IS NULL)"
                + " OR (target_type = 'Product' AND product_id IS NOT NULL AND category_id IS NULL)");

            // RN05. O CAST evita a comparação como texto no SQLite, onde decimal é gravado como TEXT.
            table.HasCheckConstraint(
                "ck_campaign_items_benefit_value",
                "CAST(benefit_value AS REAL) > 0"
                + " AND (benefit_type <> 'Percentage' OR CAST(benefit_value AS REAL) <= 100)");

            table.HasCheckConstraint("ck_campaign_items_voucher_limit", "voucher_limit IS NULL OR voucher_limit > 0");
        });

        builder.HasKey(item => item.Id);

        // O id vem da aplicação (Guid v7). Sem isso, item novo adicionado à coleção de uma campanha
        // rastreada seria tomado como existente (UPDATE) por já ter chave (US-01.2, Campaign.ReplaceItems).
        builder.Property(item => item.Id).ValueGeneratedNever();

        builder.Property(item => item.TargetType).HasConversion<string>().HasMaxLength(20);
        builder.Property(item => item.BenefitType).HasConversion<string>().HasMaxLength(20);
        builder.Property(item => item.BenefitValue).HasPrecision(10, 2);

        builder.HasOne(item => item.Campaign)
            .WithMany(campaign => campaign.Items)
            .HasForeignKey(item => item.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.Category)
            .WithMany()
            .HasForeignKey(item => item.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(item => item.Product)
            .WithMany()
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
