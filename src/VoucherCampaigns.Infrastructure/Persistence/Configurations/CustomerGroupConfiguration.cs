using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Customers;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class CustomerGroupConfiguration : IEntityTypeConfiguration<CustomerGroup>
{
    public void Configure(EntityTypeBuilder<CustomerGroup> builder)
    {
        builder.ToTable("customer_groups");
        builder.HasKey(group => group.Id);
        builder.Property(group => group.Id).ValueGeneratedNever();
        builder.Property(group => group.Name).HasMaxLength(150);
        builder.HasIndex(group => new { group.NetworkId, group.Name }).IsUnique();
        builder.HasOne<Domain.Organization.Network>()
            .WithMany()
            .HasForeignKey(group => group.NetworkId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
