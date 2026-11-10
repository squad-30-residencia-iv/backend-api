namespace VoucherCampaigns.Application.Vouchers;

/// <summary>Seção <c>Vouchers</c> da configuração (spec GH-045, D-6), validada no startup.</summary>
public record VoucherOptions
{
    public const string SectionName = "Vouchers";
    public const int DefaultReservationMinutes = 15;
    public const int DefaultReservationSweepSeconds = 60;
    public const int MaxReservationMinutes = 1440;
    public const int MaxReservationSweepSeconds = 3600;

    /// <summary>Prazo da reserva do voucher provisório (PR-17), de 1 a 1440 minutos.</summary>
    public int ReservationMinutes { get; init; } = DefaultReservationMinutes;

    /// <summary>Intervalo da varredura que libera reservas vencidas; 0 desliga a varredura.</summary>
    public int ReservationSweepSeconds { get; init; } = DefaultReservationSweepSeconds;

    public void Validate()
    {
        if (ReservationMinutes < 1 || ReservationMinutes > MaxReservationMinutes)
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{SectionName}:{nameof(ReservationMinutes)}' deve ser de 1 a {MaxReservationMinutes}.");
        }

        if (ReservationSweepSeconds < 0 || ReservationSweepSeconds > MaxReservationSweepSeconds)
        {
            throw new InvalidOperationException(
                $"Configuração inválida: '{SectionName}:{nameof(ReservationSweepSeconds)}' deve ser de 0 a {MaxReservationSweepSeconds}.");
        }
    }
}
