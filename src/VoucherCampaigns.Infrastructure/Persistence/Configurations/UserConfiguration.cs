using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Identity;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        // Os CHECKs usam os nomes snake_case das colunas e valem para SQLite e PostgreSQL.
        builder.ToTable("users", table =>
        {
            table.HasCheckConstraint(
                "ck_users_role", "role IN ('analista', 'gestor', 'administrador')");
            table.HasCheckConstraint(
                "ck_users_network",
                "(role = 'administrador' AND network_id IS NULL) OR (role <> 'administrador' AND network_id IS NOT NULL)");
        });

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();

        builder.Property(user => user.Email).HasMaxLength(254).IsRequired();
        builder.HasIndex(user => user.Email).IsUnique();
        builder.Property(user => user.Name).HasMaxLength(150).IsRequired();

        builder.Property(user => user.PasswordHash).HasMaxLength(200).IsRequired();
        builder.Property(user => user.Role).HasMaxLength(20).IsRequired();
    }
}
