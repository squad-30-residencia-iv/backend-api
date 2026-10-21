namespace VoucherCampaigns.IntegrationTests;

/// <summary>
/// Relógio da Api nos testes: usa a hora real até um teste fixar <see cref="UtcNow"/>.
/// Não afeta a validação do JWT (feita com a hora real), por isso os testes com relógio fixo
/// usam tokens de <c>TestAuth.CreateToken</c> em vez do login.
/// </summary>
public class TestClock : TimeProvider
{
    public DateTimeOffset? UtcNow { get; set; }

    public override DateTimeOffset GetUtcNow() => UtcNow ?? System.GetUtcNow();
}
