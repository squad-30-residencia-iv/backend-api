using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Infrastructure.Persistence;
using VoucherCampaigns.Infrastructure.Persistence.Seed;

namespace VoucherCampaigns.IntegrationTests;

public class DatabaseSchemaTests
{
    [Fact]
    public async Task Startup_InDevelopment_SeedsDemoData()
    {
        using var factory = new ApiFactory();

        await using var db = DbScope.Open(factory);

        Assert.Equal(1, await db.Context.Networks.CountAsync());
        Assert.Equal(1, await db.Context.Regions.CountAsync());
        Assert.Equal(3, await db.Context.Establishments.CountAsync());
        Assert.Equal(3, await db.Context.Channels.CountAsync());
        Assert.Equal(3, await db.Context.Categories.CountAsync());
        Assert.Equal(3, await db.Context.Products.CountAsync());
    }

    [Fact]
    public async Task Startup_Twice_DoesNotDuplicateSeed()
    {
        var connectionString = ApiFactory.NewSqliteConnectionString();

        try
        {
            using (var first = ApiFactory.For("Sqlite", connectionString))
            {
                _ = first.Services;
            }

            using var second = ApiFactory.For("Sqlite", connectionString);
            await using var db = DbScope.Open(second);

            Assert.Equal(1, await db.Context.Networks.CountAsync());
            Assert.Equal(3, await db.Context.Products.CountAsync());
        }
        finally
        {
            ApiFactory.DeleteSqliteDatabase(connectionString);
        }
    }

    [Fact]
    public async Task Startup_WithSeedDisabled_LeavesTablesEmpty()
    {
        var connectionString = ApiFactory.NewSqliteConnectionString();

        try
        {
            using var factory = ApiFactory.For("Sqlite", connectionString, seedDemoData: "false");
            await using var db = DbScope.Open(factory);

            Assert.False(await db.Context.Networks.AnyAsync());
            Assert.False(await db.Context.Categories.AnyAsync());
        }
        finally
        {
            ApiFactory.DeleteSqliteDatabase(connectionString);
        }
    }

    [Fact]
    public async Task Campaign_WithEndBeforeStart_IsRejected()
    {
        using var factory = new ApiFactory();
        await using var db = DbScope.Open(factory);

        var campaign = NewCampaign();
        campaign.EndsAt = campaign.StartsAt.AddDays(-1);
        db.Context.Campaigns.Add(campaign);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
    }

    [Theory]
    [InlineData(5)]
    [InlineData(100)]
    public async Task PercentageItem_UpTo100_IsAccepted(decimal percentage)
    {
        using var factory = new ApiFactory();
        await using var db = DbScope.Open(factory);

        var campaign = NewCampaign();
        campaign.Items.Add(new CampaignItem
        {
            TargetType = TargetType.All,
            BenefitType = BenefitType.Percentage,
            BenefitValue = percentage
        });
        db.Context.Campaigns.Add(campaign);

        await db.Context.SaveChangesAsync();

        Assert.Equal(1, await db.Context.CampaignItems.CountAsync());
    }

    [Theory]
    [InlineData(BenefitType.Percentage, 150)]
    [InlineData(BenefitType.Percentage, 0)]
    [InlineData(BenefitType.FixedAmount, -10)]
    public async Task Item_WithInvalidBenefit_IsRejected(BenefitType type, decimal value)
    {
        using var factory = new ApiFactory();
        await using var db = DbScope.Open(factory);

        var campaign = NewCampaign();
        campaign.Items.Add(new CampaignItem { TargetType = TargetType.All, BenefitType = type, BenefitValue = value });
        db.Context.Campaigns.Add(campaign);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task CategoryItem_WithoutCategory_IsRejected()
    {
        using var factory = new ApiFactory();
        await using var db = DbScope.Open(factory);

        var campaign = NewCampaign();
        campaign.Items.Add(new CampaignItem
        {
            TargetType = TargetType.Category,
            BenefitType = BenefitType.FixedAmount,
            BenefitValue = 20
        });
        db.Context.Campaigns.Add(campaign);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Product_WithDuplicatedCode_IsRejected()
    {
        using var factory = new ApiFactory();
        await using var db = DbScope.Open(factory);

        db.Context.Products.Add(new Product
        {
            CategoryId = DemoDataSeeder.FashionCategoryId,
            Code = "MOD-001",
            Name = "Camiseta repetida"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
    }

    /// <summary>Abre um escopo de DI e entrega o AppDbContext dele; descarta os dois juntos.</summary>
    [Fact]
    public async Task Startup_InDevelopment_SeedsNetworkWithBrasiliaTimeZone()
    {
        using var factory = new ApiFactory();
        await using var db = DbScope.Open(factory);

        var network = await db.Context.Networks.SingleAsync();

        Assert.Equal("America/Sao_Paulo", network.TimeZone);
    }

    [Fact]
    public async Task Campaign_WithPendingApprovalStatus_IsStoredAsText()
    {
        using var factory = new ApiFactory();
        await using var db = DbScope.Open(factory);

        var campaign = NewCampaign();
        campaign.Name = "Aguardando o gestor";
        campaign.Status = CampaignStatus.PendingApproval;
        db.Context.Campaigns.Add(campaign);
        await db.Context.SaveChangesAsync();

        // Filtra pelo nome: o formato do Guid no SQL puro varia por provider.
        var status = await db.Context.Database
            .SqlQuery<string>($"SELECT status AS \"Value\" FROM campaigns WHERE name = {campaign.Name}")
            .SingleAsync();

        Assert.Equal("PendingApproval", status);
    }

    private sealed class DbScope(AsyncServiceScope scope) : IAsyncDisposable
    {
        public AppDbContext Context { get; } = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        public static DbScope Open(ApiFactory factory) => new(factory.Services.CreateAsyncScope());

        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }

    private static Campaign NewCampaign() => new()
    {
        NetworkId = DemoDataSeeder.NetworkId,
        Name = "Volta às Aulas",
        StartsAt = new DateTime(2026, 11, 1, 11, 0, 0, DateTimeKind.Utc),
        EndsAt = new DateTime(2026, 11, 30, 23, 59, 0, DateTimeKind.Utc)
    };
}
