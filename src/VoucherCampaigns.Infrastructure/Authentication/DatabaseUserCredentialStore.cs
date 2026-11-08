using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Domain.Identity;
using VoucherCampaigns.Infrastructure.Persistence;

namespace VoucherCampaigns.Infrastructure.Authentication;

public class DatabaseUserCredentialStore(AppDbContext db) : IUserCredentialStore
{
    public async Task<UserAccount?> FindByCredentialsAsync(
        string email, string password, CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Email == normalized, cancellationToken);

        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
        {
            return null;
        }

        return new UserAccount(user.Id, user.Email, user.Role, user.NetworkId);
    }
}
