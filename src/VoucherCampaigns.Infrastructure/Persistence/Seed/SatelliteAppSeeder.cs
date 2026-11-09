using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Domain.Identity;
using VoucherCampaigns.Infrastructure.Authentication;

namespace VoucherCampaigns.Infrastructure.Persistence.Seed;

/// <summary>
/// Grava <c>Auth:Satellite:Apps</c> em <c>satellite_apps</c> quando a tabela ainda está vazia.
/// O segredo já entra como hash. Uma alteração posterior no appsettings não regrava a linha.
/// </summary>
public static class SatelliteAppSeeder
{
    public static void Seed(DbContext context, SatelliteAuthOptions options, TimeProvider clock)
    {
        if (options.Apps.Count == 0 || context.Set<SatelliteApp>().Any())
        {
            return;
        }

        AddApps(context, options, clock);
        context.SaveChanges();
    }

    public static async Task SeedAsync(
        DbContext context, SatelliteAuthOptions options, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (options.Apps.Count == 0 || await context.Set<SatelliteApp>().AnyAsync(cancellationToken))
        {
            return;
        }

        AddApps(context, options, clock);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static void AddApps(DbContext context, SatelliteAuthOptions options, TimeProvider clock)
    {
        var createdAt = clock.GetUtcNow();

        for (var index = 0; index < options.Apps.Count; index++)
        {
            var app = options.Apps[index];

            context.Set<SatelliteApp>().Add(new SatelliteApp
            {
                Id = Guid.CreateVersion7(createdAt.AddMilliseconds(index)),
                ClientId = app.ClientId.Trim().ToLowerInvariant(),
                Name = app.Name.Trim(),
                SecretHash = PasswordHasher.Hash(app.ClientSecret),
                NetworkId = Guid.Parse(app.NetworkId),
                Scopes = string.Join(' ', app.Scopes.Distinct()),
                IsActive = true,
                CreatedAt = createdAt.UtcDateTime
            });
        }
    }
}
