using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VoucherCampaigns.Application.SystemStatus;
using VoucherCampaigns.Infrastructure.Persistence;

namespace VoucherCampaigns.Infrastructure;

public static class DependencyInjection
{
    private const string SqliteMigrationsAssembly = "VoucherCampaigns.Infrastructure.Migrations.Sqlite";
    private const string PostgresMigrationsAssembly = "VoucherCampaigns.Infrastructure.Migrations.Postgres";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Resolvido sob demanda para enxergar a configuração final do host (appsettings, .env e variáveis de ambiente).
        services.AddSingleton(provider =>
            DatabaseOptions.FromConfiguration(provider.GetRequiredService<IConfiguration>()));

        services.AddDbContext<AppDbContext>((provider, builder) =>
        {
            var options = provider.GetRequiredService<DatabaseOptions>();

            switch (options.ResolveProvider())
            {
                case DatabaseProvider.Sqlite:
                    builder.UseSqlite(
                        options.ConnectionString,
                        sqlite => sqlite.MigrationsAssembly(SqliteMigrationsAssembly));
                    break;
                case DatabaseProvider.Postgres:
                    builder.UseNpgsql(
                        options.ConnectionString,
                        npgsql => npgsql.MigrationsAssembly(PostgresMigrationsAssembly));
                    break;
            }
        });

        services.AddScoped<IDatabaseStatusReader, DatabaseStatusReader>();

        return services;
    }
}
