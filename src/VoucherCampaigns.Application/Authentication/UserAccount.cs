namespace VoucherCampaigns.Application.Authentication;

/// <param name="NetworkId">Rede do usuário; nulo apenas para o administrador da plataforma.</param>
public record UserAccount(Guid UserId, string Email, string Role, Guid? NetworkId);
