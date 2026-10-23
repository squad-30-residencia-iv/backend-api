using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Common;

/// <summary>Normaliza campos que o MVC deixa passar (espaço em branco, Guid vazio, flag omitida).</summary>
public static class InputValues
{
    public static string RequiredText(string? value, string field, string requiredMessage, int maxLength, string lengthMessage)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new RequestValidationException(field, requiredMessage);
        }

        if (trimmed.Length > maxLength)
        {
            throw new RequestValidationException(field, lengthMessage);
        }

        return trimmed;
    }

    public static Guid RequiredId(Guid? value, string field, string message)
    {
        if (value is null || value.Value == Guid.Empty)
        {
            throw new RequestValidationException(field, message);
        }

        return value.Value;
    }

    public static bool RequiredFlag(bool? value, string field, string message) =>
        value ?? throw new RequestValidationException(field, message);

    public static string TimeZone(string? value, string requiredMessage, string lengthMessage, string invalidMessage)
    {
        const string field = "TimeZone";
        var timeZone = RequiredText(value, field, requiredMessage, 64, lengthMessage);

        try
        {
            _ = NetworkTime.Resolve(timeZone);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new RequestValidationException(field, invalidMessage);
        }

        return timeZone;
    }

    public static ChannelType ParseChannelType(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new RequestValidationException("Type", "Informe o tipo do canal.");
        }

        if (!Enum.GetNames<ChannelType>().Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            throw new RequestValidationException("Type", "Tipo de canal inválido. Use 'Physical' ou 'Virtual'.");
        }

        return Enum.Parse<ChannelType>(trimmed, ignoreCase: true);
    }
}
