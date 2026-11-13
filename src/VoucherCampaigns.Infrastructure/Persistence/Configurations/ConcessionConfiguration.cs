using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

public class ConcessionConfiguration : IEntityTypeConfiguration<Concession>
{
    public void Configure(EntityTypeBuilder<Concession> builder)
    {
        builder.ToTable("concessions");
        builder.HasKey(concession => concession.Id);
        builder.Property(concession => concession.Id).ValueGeneratedNever();

        builder.Property(concession => concession.TransactionId).HasMaxLength(100).IsRequired();
        builder.Property(concession => concession.CustomerDocument).HasMaxLength(11).IsRequired();
        builder.Property(concession => concession.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(concession => concession.TotalAmount).HasPrecision(10, 2);
        builder.Property(concession => concession.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(concession => concession.Refusal).HasConversion<string>().HasMaxLength(30);

        // Snapshot da elegibilidade (spec GH-047, D-12), em texto livre: só os atributos que algum critério usou.
        builder.Property(concession => concession.Eligibility);

        // Chave de idempotência (PR-07): uma decisão por transação em cada rede.
        builder.HasIndex(concession => new { concession.NetworkId, concession.TransactionId }).IsUnique();
        builder.HasIndex(concession => new { concession.NetworkId, concession.CustomerDocument, concession.RequestedAt });

        builder.HasOne<VoucherCampaigns.Domain.Organization.Establishment>()
            .WithMany()
            .HasForeignKey(concession => concession.EstablishmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Sem FK para satellite_apps nem para a campanha recusada: a concessão sobrevive a qualquer limpeza.
    }
}
