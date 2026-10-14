using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class NetworkConfiguration : IEntityTypeConfiguration<Network>
{
    public void Configure(EntityTypeBuilder<Network> builder)
    {
        builder.ToTable("networks");

        builder.HasKey(network => network.Id);

        builder.Property(network => network.Name).HasMaxLength(150).IsRequired();
        builder.HasIndex(network => network.Name).IsUnique();
    }
}
