using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Application.SystemStatus;
using VoucherCampaigns.Infrastructure.Authentication;
using VoucherCampaigns.Infrastructure.Persistence;
using VoucherCampaigns.Infrastructure.Persistence.Repositories;
using VoucherCampaigns.Infrastructure.Persistence.Seed;

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

            // O EF Core chama o seeding logo após aplicar as migrações (MigrateAsync no startup).
            if (options.SeedDemoData)
            {
                builder
                    .UseSeeding((context, _) => DemoDataSeeder.Seed(context))
                    .UseAsyncSeeding((context, _, cancellationToken) => DemoDataSeeder.SeedAsync(context, cancellationToken));
            }
        });

        services.AddScoped<IDatabaseStatusReader, DatabaseStatusReader>();
        services.AddScoped<INetworkRepository, NetworkRepository>();
        services.AddScoped<ICampaignRepository, CampaignRepository>();
        services.AddScoped<ICatalogRepository, CatalogRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();

        services.AddSingleton(provider =>
            AuthOptions.FromConfiguration(provider.GetRequiredService<IConfiguration>()));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IUserCredentialStore, ConfigurationUserCredentialStore>();
        services.AddScoped<IAuthService, JwtAuthService>();

        return services;
    }
}
