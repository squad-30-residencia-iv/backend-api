using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Infrastructure.Persistence;

namespace VoucherCampaigns.IntegrationTests;

/// <summary>
/// Garante que as entidades mapeadas no EF Core e as migrações dos dois bancos estão sincronizadas.
/// Se falhar, gere uma nova migração com "sh scripts/add-migration.sh NomeDaMigracao".
/// </summary>
public class MigrationsModelTests
{
    [Fact]
    public void SqliteMigrations_HaveNoPendingModelChanges()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(
                "Data Source=:memory:",
                sqlite => sqlite.MigrationsAssembly("VoucherCampaigns.Infrastructure.Migrations.Sqlite"))
            .Options;

        using var context = new AppDbContext(options);

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void PostgresMigrations_HaveNoPendingModelChanges()
    {
        // Não abre conexão: só compara o modelo com o snapshot das migrações.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=design-time",
                npgsql => npgsql.MigrationsAssembly("VoucherCampaigns.Infrastructure.Migrations.Postgres"))
            .Options;

        using var context = new AppDbContext(options);

        Assert.False(context.Database.HasPendingModelChanges());
    }
}
