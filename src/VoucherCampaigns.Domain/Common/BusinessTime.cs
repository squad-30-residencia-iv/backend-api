namespace VoucherCampaigns.Domain.Common;

/// <summary>
/// Instantes de negócio informados ao minuto (vigência, data fixa e teto da validade). Pela decisão D9,
/// "fim às 23:59" vale o minuto inteiro: até 23:59:59.
/// </summary>
public static class BusinessTime
{
    public static DateTime TruncateToMinute(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMinute, value.Kind);

    public static DateTime TruncateToSecond(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, value.Kind);

    /// <summary>Último segundo do minuto informado (23:59 → 23:59:59).</summary>
    public static DateTime LastSecondOfMinute(DateTime value) => TruncateToMinute(value).AddSeconds(59);

    /// <summary>Primeiro instante depois do minuto informado (23:59 → 00:00:00 do dia seguinte).</summary>
    public static DateTime AfterMinute(DateTime value) => TruncateToMinute(value).AddMinutes(1);

    /// <summary>Verdadeiro quando <paramref name="utcNow"/> já passou do minuto de <paramref name="endUtc"/> (D9).</summary>
    public static bool HasPassed(DateTime endUtc, DateTime utcNow) => utcNow >= AfterMinute(endUtc);
}
