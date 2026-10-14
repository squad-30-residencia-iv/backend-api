using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class RegionConfiguration : IEntityTypeConfiguration<Region>
{
    public void Configure(EntityTypeBuilder<Region> builder)
    {
        builder.ToTable("regions");

        builder.HasKey(region => region.Id);

        builder.Property(region => region.Name).HasMaxLength(150).IsRequired();
        builder.HasIndex(region => new { region.NetworkId, region.Name }).IsUnique();

        builder.HasOne(region => region.Network)
            .WithMany(network => network.Regions)
            .HasForeignKey(region => region.NetworkId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
