using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VoucherCampaigns.Domain.Common;

namespace VoucherCampaigns.Infrastructure.Persistence;

internal static class ConstraintSave
{
    public static async Task SaveAsync(
        DbContext context, string conflictCode, string conflictMessage, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new BusinessRuleException(conflictCode, conflictMessage);
        }
    }

    private static bool IsUniqueViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return true;
            }

            if (current is SqliteException { SqliteExtendedErrorCode: 2067 })
            {
                return true;
            }
        }

        return false;
    }
}
