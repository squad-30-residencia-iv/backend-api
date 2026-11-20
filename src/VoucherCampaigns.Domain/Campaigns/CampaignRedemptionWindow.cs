namespace VoucherCampaigns.Domain.Campaigns;

/// <summary>
/// Dias e horário em que o voucher pode ser usado (spec GH-070, D-3), no fuso da rede. A janela vale de
/// <see cref="StartMinute"/> até o fim do minuto <see cref="EndMinute"/>, inclusive. Com o fim antes do início, a janela
/// vira a noite: os dias são os do início, e o trecho depois da meia-noite pertence ao dia seguinte.
/// </summary>
public class CampaignRedemptionWindow
{
    public const int MinutesPerDay = 24 * 60;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    /// <summary>Máscara de bits dos dias em que a janela começa: bit <c>1 &lt;&lt; (int)DayOfWeek</c>.</summary>
    public int DaysOfWeek { get; set; }

    /// <summary>Minuto do dia em que a janela começa (0 a 1439).</summary>
    public int StartMinute { get; set; }

    /// <summary>Último minuto do dia que ainda vale (0 a 1439); menor que o início = vira a noite.</summary>
    public int EndMinute { get; set; }

    public bool Overnight => EndMinute < StartMinute;

    public static int MaskOf(IEnumerable<DayOfWeek> days) => days.Aggregate(0, (mask, day) => mask | (1 << (int)day));

    public IReadOnlyList<DayOfWeek> Days =>
        Enum.GetValues<DayOfWeek>().Where(StartsOn).ToList();

    public bool StartsOn(DayOfWeek day) => (DaysOfWeek & (1 << (int)day)) != 0;

    /// <summary>O instante local (no fuso da rede) cai na janela.</summary>
    public bool Contains(DateTime localTime)
    {
        var minute = localTime.Hour * 60 + localTime.Minute;
        var day = localTime.DayOfWeek;

        if (!Overnight)
        {
            return StartsOn(day) && minute >= StartMinute && minute <= EndMinute;
        }

        var previousDay = (DayOfWeek)(((int)day + 6) % 7);

        return (StartsOn(day) && minute >= StartMinute) || (StartsOn(previousDay) && minute <= EndMinute);
    }
}
