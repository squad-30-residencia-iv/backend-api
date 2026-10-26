namespace VoucherCampaigns.Application.Authentication;

/// <summary>Papéis oficiais, com o valor exato gravado no token.</summary>
public static class Roles
{
    public const string Analista = "analista";
    public const string Gestor = "gestor";

    /// <summary>Administrador da plataforma (PR-15): mantém a hierarquia e não pertence a uma rede.</summary>
    public const string Administrador = "administrador";

    public static readonly IReadOnlyList<string> All = [Analista, Gestor, Administrador];
}
