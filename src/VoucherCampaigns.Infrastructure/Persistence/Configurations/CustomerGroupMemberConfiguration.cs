using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Customers;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class CustomerGroupMemberConfiguration : IEntityTypeConfiguration<CustomerGroupMember>
{
    public void Configure(EntityTypeBuilder<CustomerGroupMember> builder)
    {
        builder.ToTable("customer_group_members");
        builder.HasKey(member => new { member.GroupId, member.Document });
        builder.Property(member => member.Document).HasMaxLength(11);
        builder.HasOne(member => member.Group)
            .WithMany(group => group.Members)
            .HasForeignKey(member => member.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
