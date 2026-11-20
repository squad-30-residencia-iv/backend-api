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

    [Fact]
    public void Lockout_IsPerClientAndOrigin_SoAnotherAddressDoesNotLockTheLegitimateClient()
    {
        // SEC-05 (spec GH-071, D-2): o clientId não é segredo; o bloqueio de uma origem não alcança as outras.
        for (var i = 0; i < SatelliteTokenRateLimiter.MaxFailedAttempts; i++)
        {
            _limiter.RecordFailure("pdv-loja", "203.0.113.9");
        }

        Assert.True(_limiter.IsLockedOut("pdv-loja", "203.0.113.9"));
        Assert.False(_limiter.IsLockedOut("pdv-loja", "10.0.0.5"));
    }

    [Fact]
    public void TrackedClients_StayUnderTheCap_AndExpiredEntriesMakeRoom()
    {
        var limiter = new SatelliteTokenRateLimiter(_timeProvider, maxTrackedClients: 3);

        for (var i = 0; i < 10; i++)
        {
            limiter.RecordFailure($"aleatorio-{i}", "198.51.100.1");
        }

        Assert.Equal(3, limiter.TrackedCount);

        _timeProvider.UtcNow = _timeProvider.UtcNow.Add(SatelliteTokenRateLimiter.LockoutWindow.Add(TimeSpan.FromSeconds(1)));
        limiter.RecordFailure("pdv-novo", "198.51.100.1");

        Assert.Equal(1, limiter.TrackedCount);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
