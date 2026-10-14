using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> builder)
    {
        builder.ToTable("channels");

        builder.HasKey(channel => channel.Id);

        builder.Property(channel => channel.Type).HasConversion<string>().HasMaxLength(20);

        // Cada estabelecimento tem no máximo um canal físico e um virtual.
        builder.HasIndex(channel => new { channel.EstablishmentId, channel.Type }).IsUnique();

        builder.HasOne(channel => channel.Establishment)
            .WithMany(establishment => establishment.Channels)
            .HasForeignKey(channel => channel.EstablishmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
