namespace VoucherCampaigns.Domain.Identity;

/// <summary>Conta usada no login. A senha fica só como hash.</summary>
public class User
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    /// <summary>Nulo apenas para o administrador da plataforma.</summary>
    public Guid? NetworkId { get; set; }

    public DateTime CreatedAt { get; set; }
}
