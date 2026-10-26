using VoucherCampaigns.Application.DTOs.Organization;
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

    /// <summary>Canais da loja: ao menos um, sem repetição, só <c>Physical</c> ou <c>Virtual</c> (PR-11).</summary>
    public static IReadOnlyList<ChannelType> ChannelTypes(IReadOnlyList<string?>? values)
    {
        const string field = "Channels";

        if (values is null || values.Count == 0)
        {
            throw new RequestValidationException(field, OrganizationContract.ChannelsRequired);
        }

        var types = new List<ChannelType>();

        foreach (var value in values)
        {
            var trimmed = value?.Trim() ?? string.Empty;

            if (!Enum.GetNames<ChannelType>().Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                throw new RequestValidationException(field, OrganizationContract.ChannelTypeInvalid);
            }

            types.Add(Enum.Parse<ChannelType>(trimmed, ignoreCase: true));
        }

        if (types.Distinct().Count() != types.Count)
        {
            throw new RequestValidationException(field, OrganizationContract.ChannelsRepeated);
        }

        return types.Order().ToList();
    }
}
