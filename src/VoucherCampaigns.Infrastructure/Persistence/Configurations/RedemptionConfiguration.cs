using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class RedemptionConfiguration : IEntityTypeConfiguration<Redemption>
{
    public void Configure(EntityTypeBuilder<Redemption> builder)
    {
        builder.ToTable("redemptions", table =>
            table.HasCheckConstraint("ck_redemptions_amounts", "total_amount >= 0 AND discount_amount >= 0"));

        builder.HasKey(redemption => redemption.Id);
        builder.Property(redemption => redemption.Id).ValueGeneratedNever();

        builder.Property(redemption => redemption.TransactionId).HasMaxLength(100).IsRequired();
        builder.Property(redemption => redemption.CustomerDocument).HasMaxLength(11).IsRequired();
        builder.Property(redemption => redemption.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(redemption => redemption.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(redemption => redemption.TotalAmount).HasPrecision(10, 2);
        builder.Property(redemption => redemption.DiscountAmount).HasPrecision(10, 2);

        // Um voucher por compra (US-07.3, cenário 4) e idempotência (cenário 5); o estorno não reabre a transação (D-2).
        builder.HasIndex(redemption => new { redemption.NetworkId, redemption.TransactionId }).IsUnique();
        builder.HasIndex(redemption => new { redemption.VoucherId, redemption.Status });
        builder.HasIndex(redemption => new { redemption.NetworkId, redemption.RedeemedAt });

        builder.HasOne(redemption => redemption.Voucher)
            .WithMany()
            .HasForeignKey(redemption => redemption.VoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Campaign>()
            .WithMany()
            .HasForeignKey(redemption => redemption.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Establishment>()
            .WithMany()
            .HasForeignKey(redemption => redemption.EstablishmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Sem FK para satellite_apps, como em concessions: o resgate sobrevive a qualquer limpeza.
    }
}
