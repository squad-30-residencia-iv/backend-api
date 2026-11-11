namespace VoucherCampaigns.Application.Authentication;

/// <summary>Escopos das aplicações satélites (US-07.5), com o valor exato gravado no token.</summary>
public static class SatelliteScopes
{
    /// <summary>Consultar campanhas vigentes (US-07.1).</summary>
    public const string ReadCampaigns = "campaigns:read";

    /// <summary>Conceder voucher (US-07.2, US-07.4).</summary>
    public const string IssueVouchers = "vouchers:issue";

    /// <summary>Resgatar voucher (US-07.3).</summary>
    public const string RedeemVouchers = "vouchers:redeem";

    public static readonly IReadOnlyList<string> All = [ReadCampaigns, IssueVouchers, RedeemVouchers];

    /// <summary>
    /// Normaliza o escopo informado para a forma canônica armazenada e aceita variantes comuns.
    /// </summary>
    public static bool TryNormalize(string? scope, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(scope))
        {
            return false;
        }

        var trimmed = scope.Trim().ToLowerInvariant();

        switch (trimmed)
        {
            case "campaigns:read":
            case "campaign:read":
                normalized = ReadCampaigns;
                return true;

            case "vouchers:issue":
            case "voucher:issue":
            case "voucher:read":
                normalized = IssueVouchers;
                return true;

            case "vouchers:redeem":
            case "voucher:redeem":
                normalized = RedeemVouchers;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Valida e normaliza uma lista de escopos. Retorna false se a lista estiver vazia ou contiver escopo desconhecido.
    /// </summary>
    public static bool TryNormalizeAll(IEnumerable<string>? scopes, out IReadOnlyList<string> normalized)
    {
        normalized = [];
        if (scopes is null)
        {
            return false;
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var scope in scopes)
        {
            if (!TryNormalize(scope, out var norm))
            {
                return false;
            }

            result.Add(norm);
        }

        if (result.Count == 0)
        {
            return false;
        }

        normalized = result.ToList();
        return true;
    }
}
