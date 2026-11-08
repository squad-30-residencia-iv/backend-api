using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Domain.Identity;
using VoucherCampaigns.Infrastructure.Authentication;

namespace VoucherCampaigns.Infrastructure.Persistence.Seed;

/// <summary>
/// Grava <c>Auth:MockUsers</c> em <c>users</c> quando a tabela ainda está vazia.
/// A senha já entra como hash. Uma alteração posterior no appsettings não regrava a linha.
/// </summary>
public static class UserAccountSeeder
{
    public static void Seed(DbContext context, AuthOptions options, TimeProvider clock)
    {
        if (context.Set<User>().Any())
        {
            return;
        }

        AddUsers(context, options, clock);
        context.SaveChanges();
    }

    public static async Task SeedAsync(
        DbContext context, AuthOptions options, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await context.Set<User>().AnyAsync(cancellationToken))
        {
            return;
        }

        AddUsers(context, options, clock);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static void AddUsers(DbContext context, AuthOptions options, TimeProvider clock)
    {
        var createdAt = clock.GetUtcNow().UtcDateTime;

        foreach (var mock in options.MockUsers)
        {
            var account = mock.ToAccount();

            context.Set<User>().Add(new User
            {
                Id = account.UserId,
                Email = account.Email.Trim().ToLowerInvariant(),
                PasswordHash = PasswordHasher.Hash(mock.Password),
                Role = account.Role,
                NetworkId = account.NetworkId,
                CreatedAt = createdAt
            });
        }
    }
}
