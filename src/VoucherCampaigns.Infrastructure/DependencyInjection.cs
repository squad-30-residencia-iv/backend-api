using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VoucherCampaigns.Application.Audit;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Campaigns;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Application.SystemStatus;
using VoucherCampaigns.Application.Vouchers;
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

            // A carga de usuários roda sempre. O demonstrativo de rede/catálogo só com SeedDemoData.
            builder
                .UseSeeding((context, _) => SeedDatabase(provider, context, options))
                .UseAsyncSeeding((context, _, cancellationToken) =>
                    SeedDatabaseAsync(provider, context, options, cancellationToken));
        });

        services.AddScoped<IDatabaseStatusReader, DatabaseStatusReader>();
        services.AddScoped<INetworkRepository, NetworkRepository>();
        services.AddScoped<ICampaignRepository, CampaignRepository>();
        services.AddScoped<ICatalogRepository, CatalogRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.AddScoped<IConcessionRepository, ConcessionRepository>();
        services.AddScoped<IReservationReleaser, ReservationReleaser>();
        services.AddScoped<IRedemptionRepository, RedemptionRepository>();
        services.AddScoped<IRedemptionReverser, RedemptionReverser>();
        services.AddSingleton(provider => ReadVoucherOptions(provider.GetRequiredService<IConfiguration>()));

        services.AddSingleton(provider =>
            AuthOptions.FromConfiguration(provider.GetRequiredService<IConfiguration>()));
        services.AddSingleton(provider => SatelliteAuthOptions.FromConfiguration(
            provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<AuthOptions>()));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IUserCredentialStore, DatabaseUserCredentialStore>();
        services.AddScoped<IAuthService, JwtAuthService>();
        services.AddScoped<ISatelliteCredentialStore, DatabaseSatelliteCredentialStore>();
        services.AddScoped<ISatelliteTokenService, SatelliteTokenService>();
        services.AddSingleton<ISatelliteTokenRateLimiter, SatelliteTokenRateLimiter>();
        services.AddScoped<VoucherCampaigns.Application.Identity.ISatelliteAppManagementService, VoucherCampaigns.Infrastructure.Identity.SatelliteAppManagementService>();

        return services;
    }

    /// <summary>Seção <c>Vouchers</c> (spec GH-045, D-6); ausente = padrões; valor não numérico = configuração inválida.</summary>
    private static VoucherOptions ReadVoucherOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(VoucherOptions.SectionName);
        var options = new VoucherOptions
        {
            ReservationMinutes = ReadInt(section, nameof(VoucherOptions.ReservationMinutes), VoucherOptions.DefaultReservationMinutes),
            ReservationSweepSeconds = ReadInt(section, nameof(VoucherOptions.ReservationSweepSeconds), VoucherOptions.DefaultReservationSweepSeconds)
        };

        options.Validate();

        return options;
    }

    private static int ReadInt(IConfigurationSection section, string key, int fallback)
    {
        var raw = section[key];

        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return int.TryParse(raw, out var value)
            ? value
            : throw new InvalidOperationException($"Configuração inválida: '{VoucherOptions.SectionName}:{key}' deve ser um número inteiro.");
    }

    private static void SeedDatabase(IServiceProvider provider, DbContext context, DatabaseOptions options)
    {
        var auth = provider.GetRequiredService<AuthOptions>();
        var clock = provider.GetRequiredService<TimeProvider>();
        UserAccountSeeder.Seed(context, auth, clock);
        SatelliteAppSeeder.Seed(context, provider.GetRequiredService<SatelliteAuthOptions>(), clock);

        if (options.SeedDemoData)
        {
            DemoDataSeeder.Seed(context);
        }
    }

    private static async Task SeedDatabaseAsync(
        IServiceProvider provider, DbContext context, DatabaseOptions options, CancellationToken cancellationToken)
    {
        var auth = provider.GetRequiredService<AuthOptions>();
        var clock = provider.GetRequiredService<TimeProvider>();
        await UserAccountSeeder.SeedAsync(context, auth, clock, cancellationToken);
        await SatelliteAppSeeder.SeedAsync(context, provider.GetRequiredService<SatelliteAuthOptions>(), clock, cancellationToken);

        if (options.SeedDemoData)
        {
            await DemoDataSeeder.SeedAsync(context, cancellationToken);
        }
    }
}
