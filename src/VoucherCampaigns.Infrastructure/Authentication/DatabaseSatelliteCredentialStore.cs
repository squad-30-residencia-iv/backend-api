using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Infrastructure.Persistence;

namespace VoucherCampaigns.Infrastructure.Authentication;

public class DatabaseSatelliteCredentialStore(AppDbContext db) : ISatelliteCredentialStore
{
    public async Task<SatelliteAppAccount?> FindByCredentialsAsync(
        string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        var normalized = clientId.Trim().ToLowerInvariant();
        var app = await db.SatelliteApps.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.ClientId == normalized, cancellationToken);

        // Inexistente, inativa ou segredo errado: a mesma resposta, para não revelar qual foi.
        if (app is null || !app.IsActive || !PasswordHasher.Verify(clientSecret, app.SecretHash))
        {
            return null;
        }

        var scopes = app.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return new SatelliteAppAccount(app.Id, app.ClientId, app.NetworkId, scopes);
    }
}
