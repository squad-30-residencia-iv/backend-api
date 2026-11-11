using VoucherCampaigns.Application.Vouchers;

namespace VoucherCampaigns.Api.Vouchers;

/// <summary>
/// Varredura periódica das reservas vencidas (spec GH-045, D-6). Com várias instâncias da API, cada uma varre;
/// como a liberação é um comando condicional por voucher, não há dupla devolução, só trabalho repetido.
/// </summary>
public class ReservationSweepService(
    IServiceScopeFactory scopeFactory,
    VoucherOptions options,
    TimeProvider timeProvider,
    ILogger<ReservationSweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.ReservationSweepSeconds == 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.ReservationSweepSeconds), timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var releaser = scope.ServiceProvider.GetRequiredService<IReservationReleaser>();
                    var released = await releaser.ReleaseExpiredAsync(null, timeProvider.GetUtcNow().UtcDateTime, stoppingToken);

                    if (released > 0)
                    {
                        logger.LogInformation("Reservas de voucher vencidas liberadas: {Released}.", released);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Falha ao liberar as reservas de voucher vencidas.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Encerramento da aplicação.
        }
    }
}
