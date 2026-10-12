using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.SystemStatus;

namespace VoucherCampaigns.Infrastructure.Persistence;

public class DatabaseStatusReader(AppDbContext dbContext, DatabaseOptions options) : IDatabaseStatusReader
{
    public async Task<SystemStatusResponse> ReadAsync(CancellationToken cancellationToken)
    {
        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

        IReadOnlyList<string> appliedMigrations = canConnect
            ? [.. await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)]
            : [];

        return new SystemStatusResponse(options.ResolveProvider().ToString(), canConnect, appliedMigrations);
    }
}
