using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Domain.Organization;
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
    
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<VoucherEvent> VoucherEvents => Set<VoucherEvent>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<CampaignCondition> CampaignConditions => Set<CampaignCondition>();
    public DbSet<EligibilityCriterion> EligibilityCriteria => Set<EligibilityCriterion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        modelBuilder.Entity<Voucher>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Code).IsRequired().HasMaxLength(50);
            entity.HasMany(e => e.Events).WithOne(e => e.Voucher).HasForeignKey(e => e.VoucherId);
        });

        modelBuilder.Entity<VoucherEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Details).HasColumnType("jsonb");
        });

        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Changes).HasColumnType("jsonb");
        });

        modelBuilder.Entity<CampaignCondition>(entity => entity.HasKey(e => e.Id));
        modelBuilder.Entity<EligibilityCriterion>(entity => entity.HasKey(e => e.Id));

        modelBuilder.ApplySnakeCaseNames();
    }
}