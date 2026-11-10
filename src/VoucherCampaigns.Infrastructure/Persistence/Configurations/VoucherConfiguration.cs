using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class VoucherConfiguration : IEntityTypeConfiguration<Voucher>
{
    public void Configure(EntityTypeBuilder<Voucher> builder)
    {
        builder.ToTable("vouchers", table =>
        {
            table.HasCheckConstraint("ck_vouchers_uses", "uses_count >= 0 AND uses_count <= max_uses");
            table.HasCheckConstraint("ck_vouchers_max_uses", "max_uses > 0");
        });

        builder.HasKey(voucher => voucher.Id);
        builder.Property(voucher => voucher.Id).ValueGeneratedNever();

        builder.Property(voucher => voucher.Code).HasMaxLength(VoucherCode.Length).IsRequired();
        builder.Property(voucher => voucher.CustomerDocument).HasMaxLength(11).IsRequired();
        builder.Property(voucher => voucher.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(voucher => voucher.Channel).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(voucher => voucher.Code).IsUnique();
        builder.HasIndex(voucher => new { voucher.CampaignId, voucher.Status });
        builder.HasIndex(voucher => new { voucher.NetworkId, voucher.CustomerDocument, voucher.IssuedAt });
        builder.HasIndex(voucher => new { voucher.Status, voucher.ReservedUntil });

        builder.HasOne(voucher => voucher.Campaign)
            .WithMany()
            .HasForeignKey(voucher => voucher.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(voucher => voucher.Item)
            .WithMany()
            .HasForeignKey(voucher => voucher.CampaignItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // Um voucher por concessão (US-04.1: uma compra gera no máximo um voucher).
        builder.HasOne(voucher => voucher.Concession)
            .WithOne(concession => concession.Voucher)
            .HasForeignKey<Voucher>(voucher => voucher.ConcessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(voucher => voucher.Events)
            .WithOne(voucherEvent => voucherEvent.Voucher)
            .HasForeignKey(voucherEvent => voucherEvent.VoucherId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
