namespace VoucherCampaigns.Application.Authentication;

/// <summary>Fonte das credenciais. A implementação atual lê a tabela <c>users</c>.</summary>
public interface IUserCredentialStore
{
    Task<UserAccount?> FindByCredentialsAsync(string email, string password, CancellationToken cancellationToken);
}
