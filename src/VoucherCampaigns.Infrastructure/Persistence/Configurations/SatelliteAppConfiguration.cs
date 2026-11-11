using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Identity;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class SatelliteAppConfiguration : IEntityTypeConfiguration<SatelliteApp>
{
    public void Configure(EntityTypeBuilder<SatelliteApp> builder)
    {
        // Sem FK para networks, como em users: a carga inicial pode rodar antes da rede existir.
        builder.ToTable("satellite_apps");
        builder.HasKey(app => app.Id);
        builder.Property(app => app.Id).ValueGeneratedNever();

        builder.Property(app => app.ClientId).HasMaxLength(100).IsRequired();
        builder.HasIndex(app => app.ClientId).IsUnique();

        builder.Property(app => app.Name).HasMaxLength(150).IsRequired();
        builder.Property(app => app.SecretHash).HasMaxLength(200).IsRequired();
        builder.Property(app => app.Scopes).HasMaxLength(200).IsRequired();
    }
}
