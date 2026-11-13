using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Identity;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Domain.Vouchers;
using VoucherCampaigns.Domain.Entities;

namespace VoucherCampaigns.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Network> Networks => Set<Network>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<Establishment> Establishments => Set<Establishment>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignItem> CampaignItems => Set<CampaignItem>();
    public DbSet<CampaignScope> CampaignScopes => Set<CampaignScope>();
    public DbSet<CampaignStatusEvent> CampaignStatusEvents => Set<CampaignStatusEvent>();
    
    public DbSet<Concession> Concessions => Set<Concession>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<VoucherEvent> VoucherEvents => Set<VoucherEvent>();
    public DbSet<Redemption> Redemptions => Set<Redemption>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<CampaignCondition> CampaignConditions => Set<CampaignCondition>();
    public DbSet<EligibilityCriterion> EligibilityCriteria => Set<EligibilityCriterion>();
    public DbSet<User> Users => Set<User>();
    public DbSet<SatelliteApp> SatelliteApps => Set<SatelliteApp>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Auditoria, histórico de status e histórico do voucher só aceitam inclusão (spec GH-026-trilha-de-auditoria, D-4).
    /// Alterar ou apagar um desses registros é erro de programação: nada é gravado.
    /// </summary>
    private void EnsureAppendOnly()
    {
        var changed = ChangeTracker.Entries<IAppendOnly>()
            .FirstOrDefault(entry => entry.State is EntityState.Modified or EntityState.Deleted);

        if (changed is not null)
        {
            throw new InvalidOperationException(
                $"{changed.Metadata.ClrType.Name} só aceita inclusão; o registro não pode ser alterado nem apagado.");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // EligibilityCriterion do #36 fica até a issue #47 (público restrito) definir o modelo.
        modelBuilder.Entity<EligibilityCriterion>(entity => entity.HasKey(e => e.Id));

        modelBuilder.ApplySnakeCaseNames();
    }
}