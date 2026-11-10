using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class VoucherEventConfiguration : IEntityTypeConfiguration<VoucherEvent>
{
    public void Configure(EntityTypeBuilder<VoucherEvent> builder)
    {
        builder.ToTable("voucher_events");
        builder.HasKey(voucherEvent => voucherEvent.Id);
        builder.Property(voucherEvent => voucherEvent.Id).ValueGeneratedNever();
        builder.Property(voucherEvent => voucherEvent.EventType).HasMaxLength(30).IsRequired();
        builder.Property(voucherEvent => voucherEvent.ActorType).HasConversion<string>().HasMaxLength(20);
        builder.Property(voucherEvent => voucherEvent.Payload).HasMaxLength(2000);

        builder.HasIndex(voucherEvent => new { voucherEvent.VoucherId, voucherEvent.OccurredAt });
    }
}
