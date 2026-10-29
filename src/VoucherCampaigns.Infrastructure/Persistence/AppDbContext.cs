using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Domain.Organization;

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

    public DbSet<CampaignCondition> CampaignConditions => Set<CampaignCondition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        modelBuilder.ApplySnakeCaseNames();
    }
}
