using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Worker.Tests;

public sealed class BotUndoTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public BotUndoTests()
    {
        _connection.Open();
        using var db = CreateContext();
        db.Database.Migrate();
    }

    private ExpensesDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ExpensesDbContext>().UseSqlite(_connection).Options);

    private async Task<User> AddUserAsync(long telegramUserId)
    {
        await using var db = CreateContext();
        var user = UserFactory.Create(telegramUserId, "Test");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<Transaction> AddTransactionAsync(long userId, long amountMinor, string? note = null)
    {
        await using var db = CreateContext();
        var transaction = new Transaction
        {
            UserId = userId,
            AmountMinor = amountMinor,
            Currency = "AMD",
            Note = note,
            OccurredAtUtc = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc)
        };
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction;
    }

    [Fact]
    public async Task FindLast_ReturnsTheNewestTransactionOfThatUser()
    {
        var user = await AddUserAsync(100);
        var other = await AddUserAsync(200);
        await AddTransactionAsync(user.Id, 1_000, "first");
        await AddTransactionAsync(user.Id, 2_000, "second");
        await AddTransactionAsync(other.Id, 3_000, "someone else's");

        await using var db = CreateContext();
        var last = await BotUndo.FindLastAsync(db, telegramUserId: 100, default);

        last!.Note.Should().Be("second");
        last.Describe(BotText.Ru).Should().Contain("20,00 AMD").And.Contain("second").And.StartWith("➖");
        last.Describe(BotText.En).Should().Contain("20.00 AMD");
    }

    [Fact]
    public async Task FindLast_WithoutTransactions_ReturnsNull()
    {
        await AddUserAsync(100);
        await using var db = CreateContext();

        (await BotUndo.FindLastAsync(db, 100, default)).Should().BeNull();
        (await BotUndo.FindLastAsync(db, 999, default)).Should().BeNull("unknown users have nothing to undo");
    }

    [Fact]
    public async Task Delete_RemovesOwnTransaction_Once()
    {
        var user = await AddUserAsync(100);
        var transaction = await AddTransactionAsync(user.Id, 1_500, "кофе");

        await using var db = CreateContext();

        var deleted = await BotUndo.DeleteAsync(db, 100, transaction.Id, default);
        deleted!.Id.Should().Be(transaction.Id);
        (await db.Transactions.AnyAsync()).Should().BeFalse();

        (await BotUndo.DeleteAsync(db, 100, transaction.Id, default)).Should().BeNull("a second tap finds nothing");
    }

    [Fact]
    public async Task Delete_SomeoneElsesTransaction_IsRefused()
    {
        var owner = await AddUserAsync(100);
        await AddUserAsync(200);
        var transaction = await AddTransactionAsync(owner.Id, 1_500);

        await using var db = CreateContext();

        (await BotUndo.DeleteAsync(db, telegramUserId: 200, transaction.Id, default)).Should().BeNull();
        (await db.Transactions.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("undo:42", true, 42)]
    [InlineData("undo:0", false, 0)]
    [InlineData("undo:-5", false, 0)]
    [InlineData("undo:abc", false, 0)]
    [InlineData("tx:e:42", false, 0)]
    public void Callback_IsParsedStrictly(string data, bool valid, long expectedId)
    {
        BotUndo.TryParseCallback(data, out var id).Should().Be(valid);

        if (valid)
            id.Should().Be(expectedId);
    }

    [Fact]
    public void Callback_RoundTrips()
    {
        BotUndo.TryParseCallback(BotUndo.BuildCallback(123456789), out var id).Should().BeTrue();
        id.Should().Be(123456789);
    }

    public void Dispose() => _connection.Dispose();
}
