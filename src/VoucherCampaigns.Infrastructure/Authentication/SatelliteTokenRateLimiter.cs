using System.Collections.Concurrent;
using VoucherCampaigns.Application.Authentication;

namespace VoucherCampaigns.Infrastructure.Authentication;

public class SatelliteTokenRateLimiter(TimeProvider timeProvider) : ISatelliteTokenRateLimiter
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, ClientAttemptState> _states = new(StringComparer.OrdinalIgnoreCase);

    private sealed class ClientAttemptState
    {
        public int FailedCount { get; set; }
        public DateTimeOffset FirstAttemptAt { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }

    public bool IsLockedOut(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return false;
        }

        var normalized = clientId.Trim().ToLowerInvariant();
        if (!_states.TryGetValue(normalized, out var state))
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();

        lock (state)
        {
            if (state.LockedUntil.HasValue)
            {
                if (now < state.LockedUntil.Value)
                {
                    return true;
                }

                _states.TryRemove(normalized, out _);
                return false;
            }

            if (now - state.FirstAttemptAt > LockoutWindow)
            {
                _states.TryRemove(normalized, out _);
                return false;
            }

            return false;
        }
    }

    public void RecordFailure(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        var normalized = clientId.Trim().ToLowerInvariant();
        var now = timeProvider.GetUtcNow();

        var state = _states.GetOrAdd(normalized, _ => new ClientAttemptState
        {
            FailedCount = 0,
            FirstAttemptAt = now
        });

        lock (state)
        {
            if (state.LockedUntil.HasValue && now >= state.LockedUntil.Value)
            {
                state.FailedCount = 0;
                state.LockedUntil = null;
                state.FirstAttemptAt = now;
            }
            else if (!state.LockedUntil.HasValue && now - state.FirstAttemptAt > LockoutWindow)
            {
                state.FailedCount = 0;
                state.FirstAttemptAt = now;
            }

            state.FailedCount++;

            if (state.FailedCount >= MaxFailedAttempts)
            {
                state.LockedUntil = now.Add(LockoutWindow);
            }
        }
    }

    public void Reset(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        var normalized = clientId.Trim().ToLowerInvariant();
        _states.TryRemove(normalized, out _);
    }
}
