using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class EstablishmentConfiguration : IEntityTypeConfiguration<Establishment>
{
    public void Configure(EntityTypeBuilder<Establishment> builder)
    {
        builder.ToTable("establishments");

        builder.HasKey(establishment => establishment.Id);

        builder.Property(establishment => establishment.Code).HasMaxLength(20).IsRequired();
        builder.Property(establishment => establishment.Name).HasMaxLength(150).IsRequired();
        builder.HasIndex(establishment => new { establishment.RegionId, establishment.Code }).IsUnique();

        builder.HasOne(establishment => establishment.Region)
            .WithMany(region => region.Establishments)
            .HasForeignKey(establishment => establishment.RegionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
