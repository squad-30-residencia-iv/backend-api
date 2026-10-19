namespace VoucherCampaigns.Application.Authentication;

/// <summary>Fonte das credenciais. Hoje: configuração. Amanhã: projeção local ou sistema externo.</summary>
public interface IUserCredentialStore
{
    UserAccount? FindByCredentials(string email, string password);
}
