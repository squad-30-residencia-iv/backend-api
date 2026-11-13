namespace VoucherCampaigns.Domain.Common;

/// <summary>Conferência do dígito verificador do CPF (spec GH-047, D-8).</summary>
public static class Cpf
{
    /// <summary>Verdadeiro para 11 dígitos com os dois verificadores corretos; sequências repetidas (111...) são inválidas.</summary>
    public static bool IsValid(string digits)
    {
        if (digits.Length != 11 || !digits.All(char.IsAsciiDigit) || digits.All(digit => digit == digits[0]))
        {
            return false;
        }

        return Check(digits, 9) == digits[9] - '0' && Check(digits, 10) == digits[10] - '0';
    }

    private static int Check(string digits, int length)
    {
        var sum = 0;

        for (var index = 0; index < length; index++)
        {
            sum += (digits[index] - '0') * (length + 1 - index);
        }

        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }
}
