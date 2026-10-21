namespace VoucherCampaigns.Application.Authentication;

/// <summary>Usuário autenticado da requisição atual.</summary>
public interface ICurrentUser
{
    Guid UserId { get; }

    string Role { get; }

    Guid NetworkId { get; }
}
