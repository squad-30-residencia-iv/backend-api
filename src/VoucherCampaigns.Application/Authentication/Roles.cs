namespace VoucherCampaigns.Application.Authentication;

/// <summary>Papéis oficiais, com o valor exato gravado no token.</summary>
public static class Roles
{
    public const string Analista = "analista";
    public const string Gestor = "gestor";

    public static readonly IReadOnlyList<string> All = [Analista, Gestor];
}
