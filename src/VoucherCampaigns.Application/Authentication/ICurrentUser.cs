namespace VoucherCampaigns.Application.Authentication;

/// <summary>Usuário autenticado da requisição atual.</summary>
public interface ICurrentUser
{
    Guid UserId { get; }

    string Role { get; }

    /// <summary>Administrador da plataforma: não tem rede e não passa pelo escopo de rede.</summary>
    bool IsPlatformAdmin { get; }

    /// <summary>Rede do token. Não existe para o administrador da plataforma; consulte <see cref="IsPlatformAdmin"/> antes.</summary>
    Guid NetworkId { get; }
}
