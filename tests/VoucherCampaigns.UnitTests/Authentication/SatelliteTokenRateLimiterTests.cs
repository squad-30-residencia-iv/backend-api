using VoucherCampaigns.Infrastructure.Authentication;

namespace VoucherCampaigns.UnitTests.Authentication;

public class SatelliteTokenRateLimiterTests
{
    private readonly FixedTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
    private readonly SatelliteTokenRateLimiter _limiter;

    public SatelliteTokenRateLimiterTests()
    {
        _limiter = new SatelliteTokenRateLimiter(_timeProvider);
    }

    [Fact]
    public void IsLockedOut_Initially_ReturnsFalse()
    {
        Assert.False(_limiter.IsLockedOut("client-1"));
        Assert.False(_limiter.IsLockedOut(string.Empty));
    }

    [Fact]
    public void RecordFailure_UpToMaxAttemptsMinusOne_DoesNotLockout()
    {
        const string clientId = "client-demo";

        for (var i = 0; i < SatelliteTokenRateLimiter.MaxFailedAttempts - 1; i++)
        {
            _limiter.RecordFailure(clientId);
            Assert.False(_limiter.IsLockedOut(clientId));
        }
    }

    [Fact]
    public void RecordFailure_AtMaxAttempts_LocksOutClient()
    {
        const string clientId = "client-demo";

        for (var i = 0; i < SatelliteTokenRateLimiter.MaxFailedAttempts; i++)
        {
            _limiter.RecordFailure(clientId);
        }

        Assert.True(_limiter.IsLockedOut(clientId));
    }

    [Fact]
    public void Reset_ClearsLockout()
    {
        const string clientId = "client-demo";

        for (var i = 0; i < SatelliteTokenRateLimiter.MaxFailedAttempts; i++)
        {
            _limiter.RecordFailure(clientId);
        }

        Assert.True(_limiter.IsLockedOut(clientId));

        _limiter.Reset(clientId);

        Assert.False(_limiter.IsLockedOut(clientId));
    }

    [Fact]
    public void IsLockedOut_AfterLockoutWindowExpires_ReturnsFalse()
    {
        const string clientId = "client-demo";

        for (var i = 0; i < SatelliteTokenRateLimiter.MaxFailedAttempts; i++)
        {
            _limiter.RecordFailure(clientId);
        }

        Assert.True(_limiter.IsLockedOut(clientId));

        // Avança o tempo além da janela de bloqueio
        _timeProvider.UtcNow = _timeProvider.UtcNow.Add(SatelliteTokenRateLimiter.LockoutWindow.Add(TimeSpan.FromSeconds(1)));

        Assert.False(_limiter.IsLockedOut(clientId));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
