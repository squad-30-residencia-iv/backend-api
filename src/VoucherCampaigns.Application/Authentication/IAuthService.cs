namespace VoucherCampaigns.Application.Authentication;

public interface IAuthService
{
    /// <returns>null quando as credenciais são inválidas.</returns>
    TokenResponse? Authenticate(LoginRequest request);
}
