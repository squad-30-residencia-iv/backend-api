namespace VoucherCampaigns.Application.Vouchers;

/// <summary>Item da listagem de vouchers no portal (GH-050 Fatia 1).</summary>
public record PortalVoucherListItemResponse(
    Guid Id,
    string Code,
    Guid CampaignId,
    string CampaignName,
    string? CustomerName,
    string CustomerDocumentMasked,
    DateTimeOffset IssuedAt,
    DateTimeOffset ValidUntil,
    int UsesCount,
    int MaxUses,
    string Status);

public record PortalVoucherScopeItemResponse(string Label, string Value);

public record PortalVoucherEventResponse(
    Guid Id,
    string Type,
    DateTimeOffset OccurredAt,
    string Origin,
    string Place,
    string Result);

public record PortalVoucherDetailResponse(
    PortalVoucherListItemResponse Voucher,
    string Benefit,
    IReadOnlyList<PortalVoucherScopeItemResponse> Scope,
    IReadOnlyList<PortalVoucherEventResponse> Events);

public static class PortalVoucherContract
{
    public const string NetworkRequired = "Informe a rede.";
    public const string StatusInvalid =
        "Status inválido. Use Available, Redeemed, Expired, Cancelled ou Provisional.";
    public const string VoucherNotFound = "Voucher não encontrado.";
    public const string IssuedRangeInvalid = "O início do período de emissão não pode ser posterior ao fim.";
}

/// <summary>Máscara de CPF para o portal: nunca devolve os 11 dígitos.</summary>
public static class CustomerDocumentMask
{
    public static string Mask(string digits)
    {
        if (string.IsNullOrEmpty(digits) || digits.Length < 9)
        {
            return "***.***.***-**";
        }

        return $"***.{digits[3..6]}.{digits[6..9]}-**";
    }
}
