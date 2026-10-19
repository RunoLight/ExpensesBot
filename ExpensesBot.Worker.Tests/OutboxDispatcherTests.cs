using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExpensesBot.Worker.Tests;

public sealed class OutboxDispatcherTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public OutboxDispatcherTests()
    {
        _connection.Open();
        using var db = CreateContext();
        db.Database.Migrate();
    }

    private ExpensesDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ExpensesDbContext>().UseSqlite(_connection).Options);

    private async Task<OutboxMessage> QueueAsync(long telegramUserId, string text, DateTime? dueAtUtc = null)
    {
        await using var db = CreateContext();
        var user = UserFactory.Create(telegramUserId, "Test");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var message = OutboxMessage.Create(user.Id, "test", text, Now);
        message.NextAttemptAtUtc = dueAtUtc ?? Now;
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();
        return message;
    }

    private async Task<int> DispatchAsync(Func<long, string, DeliveryResult> send, DateTime? nowUtc = null)
    {
        await using var db = CreateContext();
        return await OutboxDispatcher.DispatchDueAsync(
            db, (chatId, text, _) => Task.FromResult(send(chatId, text)), nowUtc ?? Now, NullLogger.Instance, default);
    }

    private async Task<OutboxMessage> ReloadAsync(long id)
    {
        await using var db = CreateContext();
        return await db.OutboxMessages.SingleAsync(x => x.Id == id);
    }

    [Fact]
    public async Task DueMessage_IsSentToTheUsersChat_AndMarkedSent()
    {
        var queued = await QueueAsync(555, "hello");
        var sent = new List<(long ChatId, string Text)>();

        await DispatchAsync((chatId, text) =>
        {
            sent.Add((chatId, text));
            return new DeliveryResult(DeliveryStatus.Delivered);
        });

        sent.Should().Equal((555L, "hello"));
        var message = await ReloadAsync(queued.Id);
        message.Status.Should().Be(OutboxStatus.Sent);
        message.SentAtUtc.Should().Be(Now);
    }

    [Fact]
    public async Task SentMessage_IsNotSentAgain()
    {
        await QueueAsync(1, "once");
        var calls = 0;

        await DispatchAsync((_, _) => { calls++; return new DeliveryResult(DeliveryStatus.Delivered); });
        await DispatchAsync((_, _) => { calls++; return new DeliveryResult(DeliveryStatus.Delivered); });

        calls.Should().Be(1);
    }

    [Fact]
    public async Task TransientFailure_IsRetriedAfterBackoff()
    {
        var queued = await QueueAsync(1, "retry me");

        await DispatchAsync((_, _) => new DeliveryResult(DeliveryStatus.RetryLater, "502 Bad Gateway"));

        var message = await ReloadAsync(queued.Id);
        message.Status.Should().Be(OutboxStatus.Pending);
        message.Attempts.Should().Be(1);
        message.LastError.Should().Be("502 Bad Gateway");
        message.NextAttemptAtUtc.Should().Be(Now + OutboxDispatcher.Backoff(1));

        // Not due yet: nothing is attempted.
        (await DispatchAsync((_, _) => throw new InvalidOperationException("must not send"))).Should().Be(0);

        await DispatchAsync((_, _) => new DeliveryResult(DeliveryStatus.Delivered), message.NextAttemptAtUtc);
        (await ReloadAsync(queued.Id)).Status.Should().Be(OutboxStatus.Sent);
    }

    [Fact]
    public async Task RateLimit_WaitsForTelegramsRetryAfter()
    {
        var queued = await QueueAsync(1, "slow down");

        await DispatchAsync((_, _) => new DeliveryResult(DeliveryStatus.RetryLater, "429", TimeSpan.FromSeconds(7)));

        (await ReloadAsync(queued.Id)).NextAttemptAtUtc.Should().Be(Now.AddSeconds(7));
    }

    [Fact]
    public async Task PermanentRejection_IsNotRetried()
    {
        var queued = await QueueAsync(1, "blocked");

        await DispatchAsync((_, _) => new DeliveryResult(DeliveryStatus.Rejected, "403 bot was blocked"));

        (await ReloadAsync(queued.Id)).Status.Should().Be(OutboxStatus.Failed);
    }

    [Fact]
    public void Apply_GivesUpAfterMaxAttempts()
    {
        var message = OutboxMessage.Create(1, "test", "text", Now);
        message.Attempts = OutboxDispatcher.MaxAttempts - 1;

        OutboxDispatcher.Apply(message, new DeliveryResult(DeliveryStatus.RetryLater, "500"), Now);

        message.Status.Should().Be(OutboxStatus.Failed);
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(4, 240)]
    [InlineData(20, 3600)]
    public void Backoff_DoublesUpToAnHour(int attempts, int expectedSeconds)
    {
        OutboxDispatcher.Backoff(attempts).Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public async Task Prune_RemovesOldFinishedMessagesOnly()
    {
        var old = await QueueAsync(1, "old sent");
        var pending = await QueueAsync(2, "old pending");
        await using (var db = CreateContext())
        {
            var message = await db.OutboxMessages.SingleAsync(x => x.Id == old.Id);
            message.Status = OutboxStatus.Sent;
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext())
        {
            var deleted = await OutboxDispatcher.PruneAsync(db, Now + OutboxDispatcher.Retention + TimeSpan.FromDays(1), default);
            deleted.Should().Be(1);
            (await db.OutboxMessages.Select(x => x.Id).ToListAsync()).Should().Equal(pending.Id);
        }
    }

    [Theory]
    [InlineData("""{"ok":false,"error_code":429,"parameters":{"retry_after":12}}""", 12)]
    [InlineData("""{"ok":false,"error_code":500}""", null)]
    [InlineData("not json", null)]
    public void ReadRetryAfter_ParsesTelegramErrorBody(string body, int? expectedSeconds)
    {
        TelegramSender.ReadRetryAfter(body).Should().Be(expectedSeconds is { } s ? TimeSpan.FromSeconds(s) : null);
    }

    public void Dispose() => _connection.Dispose();
}
