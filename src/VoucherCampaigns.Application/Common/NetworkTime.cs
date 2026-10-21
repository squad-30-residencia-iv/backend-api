namespace VoucherCampaigns.Application.Common;

/// <summary>Conversões entre a hora local de uma rede (fuso IANA) e UTC.</summary>
public static class NetworkTime
{
    public const string InvalidLocalTimeMessage = "O horário informado não existe no fuso da rede.";

    public static TimeZoneInfo Resolve(string timeZoneId) => TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

    /// <exception cref="RequestValidationException">Horário inexistente no fuso (ex.: salto do horário de verão).</exception>
    public static DateTime ToUtc(DateTime localDateTime, TimeZoneInfo timeZone, string field)
    {
        if (localDateTime.Kind != DateTimeKind.Unspecified)
        {
            throw new ArgumentException("A hora local não pode ter fuso.", nameof(localDateTime));
        }

        if (timeZone.IsInvalidTime(localDateTime))
        {
            throw new RequestValidationException(field, InvalidLocalTimeMessage);
        }

        return TimeZoneInfo.ConvertTimeToUtc(localDateTime, timeZone);
    }

    public static DateTimeOffset ToLocal(DateTime utcDateTime, TimeZoneInfo timeZone)
    {
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone), timeZone.GetUtcOffset(utc));
    }
}
