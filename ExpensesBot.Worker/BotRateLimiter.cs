using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace ExpensesBot.Worker;

/// <summary>
/// Per-user token bucket for incoming bot updates, so one chat flooding the bot
/// (or a stuck client re-sending callbacks) can't monopolise the single polling
/// loop or the database. Over the limit, updates are dropped; the user is told
/// at most once per <see cref="WarningInterval"/>.
/// </summary>
public sealed class BotRateLimiter(TimeProvider timeProvider) : IDisposable
{
    /// <summary>Updates a user may send in a burst.</summary>
    public const int Burst = 10;

    /// <summary>Steady-state updates per minute.</summary>
    public const int PerMinute = 30;

    public static readonly TimeSpan WarningInterval = TimeSpan.FromMinutes(1);

    private readonly PartitionedRateLimiter<long> _limiter = PartitionedRateLimiter.Create<long, long>(userId =>
        RateLimitPartition.GetTokenBucketLimiter(userId, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = Burst,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1.0 / PerMinute),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    private readonly ConcurrentDictionary<long, DateTimeOffset> _lastWarning = new();

    /// <param name="shouldWarn">True for the first rejection in a while: tell the user to slow down.</param>
    /// <returns>True when the update may be processed.</returns>
    public bool TryAcquire(long telegramUserId, out bool shouldWarn)
    {
        using var lease = _limiter.AttemptAcquire(telegramUserId);
        shouldWarn = false;

        if (lease.IsAcquired)
            return true;

        var now = timeProvider.GetUtcNow();
        var last = _lastWarning.GetValueOrDefault(telegramUserId, DateTimeOffset.MinValue);
        if (now - last >= WarningInterval)
        {
            _lastWarning[telegramUserId] = now;
            shouldWarn = true;
        }

        return false;
    }

    public void Dispose() => _limiter.Dispose();
}
