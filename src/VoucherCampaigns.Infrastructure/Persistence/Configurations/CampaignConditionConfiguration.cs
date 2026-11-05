using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class CampaignConditionConfiguration : IEntityTypeConfiguration<CampaignCondition>
{
    public void Configure(EntityTypeBuilder<CampaignCondition> builder)
    {
        // Cada tipo com só o seu parâmetro, positivo. Os tipos indisponíveis (D-2) ficam fora até terem semântica.
        // O CAST evita a comparação como texto no SQLite, onde decimal é gravado como TEXT.
        builder.ToTable("campaign_conditions", table => table.HasCheckConstraint(
            "ck_campaign_conditions_parameters",
            "(type = 'PurchaseTotal' AND min_amount IS NOT NULL AND CAST(min_amount AS REAL) > 0"
            + " AND min_quantity IS NULL AND category_id IS NULL AND product_id IS NULL)"
            + " OR (type = 'ItemQuantity' AND min_quantity IS NOT NULL AND min_quantity > 0"
            + " AND min_amount IS NULL AND category_id IS NULL AND product_id IS NULL)"
            + " OR (type = 'Category' AND category_id IS NOT NULL"
            + " AND min_amount IS NULL AND min_quantity IS NULL AND product_id IS NULL)"
            + " OR (type = 'Product' AND product_id IS NOT NULL"
            + " AND min_amount IS NULL AND min_quantity IS NULL AND category_id IS NULL)"));

        builder.HasKey(condition => condition.Id);

        // Mesmo motivo do CampaignItem: o id vem da aplicação e a condição nova não pode virar UPDATE.
        builder.Property(condition => condition.Id).ValueGeneratedNever();

        builder.Property(condition => condition.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(condition => condition.MinAmount).HasPrecision(10, 2);

        builder.HasOne(condition => condition.Campaign)
            .WithMany(campaign => campaign.Conditions)
            .HasForeignKey(condition => condition.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(condition => condition.Category)
            .WithMany()
            .HasForeignKey(condition => condition.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(condition => condition.Product)
            .WithMany()
            .HasForeignKey(condition => condition.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
