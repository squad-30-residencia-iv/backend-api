using System.Security.Cryptography;
using System.Text;
using VoucherCampaigns.Application.Authentication;

namespace VoucherCampaigns.Infrastructure.Authentication;

/// <summary>Credenciais provisórias lidas de <c>Auth:MockUsers</c>.</summary>
public class ConfigurationUserCredentialStore(AuthOptions options) : IUserCredentialStore
{
    public UserAccount? FindByCredentials(string email, string password)
    {
        var user = options.MockUsers.FirstOrDefault(candidate =>
            string.Equals(candidate.Email.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));

        return user is not null && PasswordMatches(user.Password, password) ? user.ToAccount() : null;
    }

    // Compara os hashes para que o tempo não dependa do tamanho nem do conteúdo da senha.
    private static bool PasswordMatches(string expected, string informed) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(informed)));
}
