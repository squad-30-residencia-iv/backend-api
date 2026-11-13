namespace VoucherCampaigns.Application.Audit;

/// <summary>
/// Ids v7 crescentes para registros gravados no mesmo instante (relógio fixo ou várias gravações num só pedido):
/// o instante entra no id com um deslocamento de milissegundos em sequência, para a ordem de gravação ser a
/// ordem dos ids. A ordenação principal continua sendo <c>occurred_at</c>; o id só desempata.
/// </summary>
public static class AuditIds
{
    private static int _sequence;

    public static Guid Next(DateTime occurredAt) =>
        Guid.CreateVersion7(new DateTimeOffset(occurredAt, TimeSpan.Zero).AddMilliseconds(Interlocked.Increment(ref _sequence) & 0xFFFF));
}
