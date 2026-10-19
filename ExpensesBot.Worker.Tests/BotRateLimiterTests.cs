using FluentAssertions;

namespace ExpensesBot.Worker.Tests;

public sealed class BotRateLimiterTests : IDisposable
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly ManualClock _clock = new();
    private readonly BotRateLimiter _limiter;

    public BotRateLimiterTests() => _limiter = new BotRateLimiter(_clock);

    private void UseWholeBurst(long userId)
    {
        for (var i = 0; i < BotRateLimiter.Burst; i++)
            _limiter.TryAcquire(userId, out _).Should().BeTrue();
    }

    [Fact]
    public void Burst_IsAllowed_ThenUpdatesAreDropped()
    {
        UseWholeBurst(1);

        _limiter.TryAcquire(1, out _).Should().BeFalse();
    }

    [Fact]
    public void Users_HaveSeparateBuckets()
    {
        UseWholeBurst(1);

        _limiter.TryAcquire(2, out _).Should().BeTrue();
    }

    [Fact]
    public void Warning_IsSentOncePerInterval()
    {
        UseWholeBurst(1);

        _limiter.TryAcquire(1, out var first);
        _limiter.TryAcquire(1, out var second);
        _clock.Now += BotRateLimiter.WarningInterval;
        _limiter.TryAcquire(1, out var later);

        first.Should().BeTrue();
        second.Should().BeFalse("the user was just told");
        later.Should().BeTrue();
    }

    public void Dispose() => _limiter.Dispose();
}
