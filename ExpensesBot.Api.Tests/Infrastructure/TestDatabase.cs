using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Api.Tests.Infrastructure;

/// <summary>
/// A private in-memory SQLite database with the real migrations applied,
/// for testing application services without HTTP.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TestDatabase()
    {
        _connection.Open();
        using var db = CreateContext();
        db.Database.Migrate();
    }

    public ExpensesDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ExpensesDbContext>().UseSqlite(_connection).Options);

    public async Task<User> AddUserAsync(long telegramUserId = 1, string? language = null)
    {
        await using var db = CreateContext();
        var user = UserFactory.Create(telegramUserId, $"User {telegramUserId}", language);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public async Task AddAsync(params object[] entities)
    {
        await using var db = CreateContext();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    public void Dispose() => _connection.Dispose();
}

/// <summary>A clock frozen at a given instant.</summary>
public sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
}
