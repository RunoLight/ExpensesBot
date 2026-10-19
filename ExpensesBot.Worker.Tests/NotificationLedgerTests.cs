using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Worker.Tests;

public sealed class NotificationLedgerTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 18, 17, 5, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public NotificationLedgerTests()
    {
        _connection.Open();
        using var db = CreateContext();
        db.Database.Migrate();
    }

    private ExpensesDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ExpensesDbContext>().UseSqlite(_connection).Options);

    private static OutboxMessage Message(long userId, DateTime nowUtc) =>
        OutboxMessage.Create(userId, NotificationTexts.DailyReminderKind, "text", nowUtc);

    private async Task<long> AddUserAsync(long telegramUserId)
    {
        await using var db = CreateContext();
        var user = UserFactory.Create(telegramUserId, "Test");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task MarkedNotification_IsRememberedAcrossContexts()
    {
        var userId = await AddUserAsync(1);
        var today = NotificationLedger.DateKey(Now);

        await using (var db = CreateContext())
            await new NotificationLedger(db).EnqueueOnceAsync(userId, NotificationKind.DailyReminder, today, Message(userId, Now), default);

        // A new context = what the Worker sees after a restart.
        await using var restarted = CreateContext();
        var ledger = new NotificationLedger(restarted);

        (await ledger.WasSentAsync(userId, NotificationKind.DailyReminder, today, default)).Should().BeTrue();
        (await ledger.WasSentAsync(userId, NotificationKind.WeeklyReport, today, default)).Should().BeFalse();
        (await ledger.WasSentAsync(userId, NotificationKind.DailyReminder, NotificationLedger.DateKey(Now.AddDays(1)), default))
            .Should().BeFalse("tomorrow's reminder hasn't been sent yet");
    }

    [Fact]
    public async Task QueueingTwice_QueuesOneMessage()
    {
        var userId = await AddUserAsync(1);
        var key = NotificationLedger.BudgetKey(42);

        await using var db = CreateContext();
        var ledger = new NotificationLedger(db);

        await ledger.EnqueueOnceAsync(userId, NotificationKind.BudgetAlert, key, Message(userId, Now), default);
        var again = () => ledger.EnqueueOnceAsync(userId, NotificationKind.BudgetAlert, key, Message(userId, Now), default);

        (await again()).Should().BeFalse();
        (await db.NotificationLogs.CountAsync()).Should().Be(1);
        (await db.OutboxMessages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Entries_ArePerUser()
    {
        var first = await AddUserAsync(1);
        var second = await AddUserAsync(2);
        var today = NotificationLedger.DateKey(Now);

        await using var db = CreateContext();
        var ledger = new NotificationLedger(db);
        await ledger.EnqueueOnceAsync(first, NotificationKind.DailyReminder, today, Message(first, Now), default);

        (await ledger.WasSentAsync(second, NotificationKind.DailyReminder, today, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Prune_RemovesOldRemindersButKeepsBudgetAlerts()
    {
        var userId = await AddUserAsync(1);
        var old = Now.AddDays(-45);

        await using var db = CreateContext();
        var ledger = new NotificationLedger(db);
        await ledger.EnqueueOnceAsync(userId, NotificationKind.DailyReminder, NotificationLedger.DateKey(old), Message(userId, old), default);
        await ledger.EnqueueOnceAsync(userId, NotificationKind.BudgetAlert, NotificationLedger.BudgetKey(7), Message(userId, old), default);
        await ledger.EnqueueOnceAsync(userId, NotificationKind.DailyReminder, NotificationLedger.DateKey(Now), Message(userId, Now), default);

        var deleted = await ledger.PruneAsync(Now, default);

        deleted.Should().Be(1);
        (await db.NotificationLogs.Select(x => x.Kind).ToListAsync())
            .Should().BeEquivalentTo(new[] { NotificationKind.BudgetAlert, NotificationKind.DailyReminder });
    }

    [Theory]
    [InlineData(403, true)]
    [InlineData(400, true)]
    [InlineData(429, false)]
    [InlineData(500, false)]
    [InlineData(502, false)]
    public void IsPermanentFailure_RetriesOnlyTransientErrors(int status, bool permanent)
    {
        TelegramSender.IsPermanentFailure(status).Should().Be(permanent);
    }

    public void Dispose() => _connection.Dispose();
}
