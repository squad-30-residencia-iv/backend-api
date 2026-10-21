using VoucherCampaigns.Application.Common;

namespace VoucherCampaigns.UnitTests.Campaigns;

public class NetworkTimeTests
{
    private static readonly TimeZoneInfo Brasilia = NetworkTime.Resolve("America/Sao_Paulo");

    [Fact]
    public void ToUtc_InterpretsLocalTimeInNetworkTimeZone()
    {
        var utc = NetworkTime.ToUtc(new DateTime(2026, 10, 1, 8, 0, 0), Brasilia, "StartsAt");

        Assert.Equal(new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc), utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    public void ToUtc_WithDateThatHasTimeZone_Throws(DateTimeKind kind)
    {
        Assert.Throws<ArgumentException>(() => NetworkTime.ToUtc(new DateTime(2026, 10, 1, 8, 0, 0, kind), Brasilia, "StartsAt"));
    }

    [Fact]
    public void ToUtc_WithTimeSkippedByDaylightSaving_ThrowsValidation()
    {
        // Em Nova York, 08/03/2026 02:30 não existe (o relógio pula de 02:00 para 03:00).
        var newYork = NetworkTime.Resolve("America/New_York");

        var exception = Assert.Throws<RequestValidationException>(() =>
            NetworkTime.ToUtc(new DateTime(2026, 3, 8, 2, 30, 0), newYork, "StartsAt"));

        Assert.Equal("StartsAt", exception.Field);
        Assert.Equal(NetworkTime.InvalidLocalTimeMessage, exception.Message);
    }

    [Fact]
    public void ToLocal_ReturnsDateWithNetworkOffset()
    {
        var local = NetworkTime.ToLocal(new DateTime(2026, 11, 1, 2, 59, 0, DateTimeKind.Utc), Brasilia);

        Assert.Equal(new DateTime(2026, 10, 31, 23, 59, 0), local.DateTime);
        Assert.Equal(TimeSpan.FromHours(-3), local.Offset);
    }
}
