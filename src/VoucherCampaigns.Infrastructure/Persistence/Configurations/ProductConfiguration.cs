using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Catalog;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(product => product.Id);

        builder.Property(product => product.Code).HasMaxLength(30).IsRequired();
        builder.Property(product => product.Name).HasMaxLength(150).IsRequired();
        builder.HasIndex(product => product.Code).IsUnique();

        builder.HasOne(product => product.Category)
            .WithMany(category => category.Products)
            .HasForeignKey(product => product.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
