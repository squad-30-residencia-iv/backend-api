using System.Collections.Concurrent;
using VoucherCampaigns.Application.Authentication;

namespace VoucherCampaigns.Infrastructure.Authentication;

/// <summary>
/// Bloqueio do <c>POST /satellite/token</c> depois de <see cref="MaxFailedAttempts"/> segredos errados em
/// <see cref="LockoutWindow"/>, por <c>clientId</c> + origem (spec GH-071, D-2; SEC-05 da auditoria). O dicionário tem
/// teto: acima dele, as entradas vencidas são descartadas, e a falha nova de uma chave desconhecida deixa de ser contada
/// aqui (o limite nativo por IP continua valendo). O estado é desta instância (PA-1 da spec).
/// </summary>
public class SatelliteTokenRateLimiter(TimeProvider timeProvider, int maxTrackedClients = SatelliteTokenRateLimiter.MaxTrackedClients)
    : ISatelliteTokenRateLimiter
{
    public const int MaxFailedAttempts = 5;
    public const int MaxTrackedClients = 10_000;
    public static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, ClientAttemptState> _states = new(StringComparer.Ordinal);

    private sealed class ClientAttemptState
    {
        public int FailedCount { get; set; }
        public DateTimeOffset FirstAttemptAt { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }

    /// <summary>Quantas chaves estão sendo acompanhadas (para os testes do teto).</summary>
    public int TrackedCount => _states.Count;

    public bool IsLockedOut(string clientId, string? origin = null)
    {
        if (Key(clientId, origin) is not { } key || !_states.TryGetValue(key, out var state))
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();

        lock (state)
        {
            if (state.LockedUntil is { } lockedUntil)
            {
                if (now < lockedUntil)
                {
                    return true;
                }

                _states.TryRemove(key, out _);
                return false;
            }

            if (now - state.FirstAttemptAt > LockoutWindow)
            {
                _states.TryRemove(key, out _);
            }

            return false;
        }
    }

    public void RecordFailure(string clientId, string? origin = null)
    {
        if (Key(clientId, origin) is not { } key)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        if (!_states.ContainsKey(key) && _states.Count >= maxTrackedClients)
        {
            RemoveExpired(now);

            if (_states.Count >= maxTrackedClients)
            {
                return;
            }
        }

        var state = _states.GetOrAdd(key, _ => new ClientAttemptState { FailedCount = 0, FirstAttemptAt = now });

        lock (state)
        {
            if (state.LockedUntil is { } lockedUntil && now >= lockedUntil)
            {
                state.FailedCount = 0;
                state.LockedUntil = null;
                state.FirstAttemptAt = now;
            }
            else if (state.LockedUntil is null && now - state.FirstAttemptAt > LockoutWindow)
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

    public void Reset(string clientId, string? origin = null)
    {
        if (Key(clientId, origin) is { } key)
        {
            _states.TryRemove(key, out _);
        }
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var (key, state) in _states)
        {
            bool expired;

            lock (state)
            {
                expired = state.LockedUntil is { } lockedUntil
                    ? now >= lockedUntil
                    : now - state.FirstAttemptAt > LockoutWindow;
            }

            if (expired)
            {
                _states.TryRemove(key, out _);
            }
        }
    }

    private static string? Key(string clientId, string? origin) =>
        string.IsNullOrWhiteSpace(clientId) ? null : $"{clientId.Trim().ToLowerInvariant()}|{origin ?? "unknown"}";
}
