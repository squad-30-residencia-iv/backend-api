using System.Security.Cryptography;

namespace VoucherCampaigns.Domain.Vouchers;

/// <summary>Código único do voucher (US-04.3, cenário 4): 12 caracteres aleatórios em Crockford base32 (sem I, L, O e U).</summary>
public static class VoucherCode
{
    public const int Length = 12;

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Generate()
    {
        var random = RandomNumberGenerator.GetBytes(Length);

        return string.Create(Length, random, static (span, bytes) =>
        {
            for (var index = 0; index < span.Length; index++)
            {
                span[index] = Alphabet[bytes[index] % Alphabet.Length];
            }
        });
    }
}
