namespace VoucherCampaigns.Application.Authentication;

public interface IAuthService
{
    /// <returns>null quando as credenciais são inválidas.</returns>
    Task<TokenResponse?> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken);
}
