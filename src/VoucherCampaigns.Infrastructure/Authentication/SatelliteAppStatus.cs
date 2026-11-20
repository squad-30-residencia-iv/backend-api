using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Infrastructure.Persistence;

namespace VoucherCampaigns.Infrastructure.Authentication;

/// <summary>
/// Confere o carimbo do token com o banco, guardando o estado por <see cref="SatelliteAuthOptions.StampCacheSeconds"/>
/// (spec GH-071, D-1). A aplicação inexistente também fica guardada, para um token forjado não virar uma consulta por requisição.
/// </summary>
public class SatelliteAppStatus(AppDbContext db, IMemoryCache cache, SatelliteAuthOptions options) : ISatelliteAppStatus
{
    private sealed record State(bool IsActive, string SecurityStamp);

    private static readonly State Missing = new(false, string.Empty);

    public async Task<bool> IsCurrentAsync(Guid applicationId, string? stamp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(stamp))
        {
            return false;
        }

        var state = await LoadAsync(applicationId, cancellationToken);

        return state.IsActive && string.Equals(state.SecurityStamp, stamp, StringComparison.Ordinal);
    }

    public void Forget(Guid applicationId) => cache.Remove(Key(applicationId));

    private async Task<State> LoadAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        if (options.StampCacheSeconds > 0 && cache.TryGetValue(Key(applicationId), out State? cached) && cached is not null)
        {
            return cached;
        }

        var state = await db.SatelliteApps.AsNoTracking()
            .Where(app => app.Id == applicationId)
            .Select(app => new State(app.IsActive, app.SecurityStamp))
            .FirstOrDefaultAsync(cancellationToken) ?? Missing;

        if (options.StampCacheSeconds > 0)
        {
            cache.Set(Key(applicationId), state, TimeSpan.FromSeconds(options.StampCacheSeconds));
        }

        return state;
    }

    private static string Key(Guid applicationId) => $"satellite-app-status:{applicationId}";
}
