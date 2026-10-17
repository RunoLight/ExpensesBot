using ExpensesBot.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Tests;

public class CurrenciesTests
{
    [Theory]
    [InlineData("AMD", "AMD")]
    [InlineData(" usd ", "USD")]
    [InlineData("Rub", "RUB")]
    public void TryNormalize_AcceptsSupportedCurrencies(string input, string expected)
    {
        Currencies.TryNormalize(input, out var code).Should().BeTrue();
        code.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("EUR")]
    [InlineData("US")]
    public void TryNormalize_RejectsEverythingElse(string? input)
    {
        Currencies.TryNormalize(input, out _).Should().BeFalse();
    }
}

public class UserFactoryTests
{
    [Fact]
    public void Create_GivesEveryNewUserTheSameDefaults()
    {
        var user = UserFactory.Create(42, "  Анна  ");

        user.TelegramUserId.Should().Be(42);
        user.DisplayName.Should().Be("Анна");
        user.DefaultCurrency.Should().Be(Currencies.Default);
        user.Categories.Select(c => c.Name).Should().Contain(new[] { "Продукты", "Зарплата", "Другое" });
    }

    [Fact]
    public void Create_BlankNameBecomesNull()
    {
        UserFactory.Create(42, " ").DisplayName.Should().BeNull();
    }
}

public class DatabaseConnectionTests
{
    [Fact]
    public void Resolve_MissingConnectionString_ThrowsHelpfulError()
    {
        var act = () => DatabaseConnection.Resolve(null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ExpensesDatabase*");
    }

    [Fact]
    public void Resolve_RelativePath_BecomesAbsolute()
    {
        var resolved = new SqliteConnectionStringBuilder(
            DatabaseConnection.Resolve("Data Source=data/expenses-test.db"));

        Path.IsPathRooted(resolved.DataSource).Should().BeTrue();
        resolved.DataSource.Should().EndWith(Path.Combine("data", "expenses-test.db"));
    }

    [Theory]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=shared-test;Mode=Memory;Cache=Shared")]
    public void Resolve_InMemoryDatabase_IsLeftAlone(string connectionString)
    {
        var resolved = new SqliteConnectionStringBuilder(DatabaseConnection.Resolve(connectionString));
        var original = new SqliteConnectionStringBuilder(connectionString);

        resolved.DataSource.Should().Be(original.DataSource);
    }
}

public class MigrationsTests
{
    [Fact]
    public void Migrations_MatchTheModel()
    {
        // Fails when an entity changes without a new migration
        // (Database.Migrate() would then throw at startup).
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ExpensesDbContext>()
            .UseSqlite(connection)
            .Options;

        using var db = new ExpensesDbContext(options);

        db.Database.HasPendingModelChanges().Should().BeFalse();
    }

    [Fact]
    public void Migrations_ApplyToAnEmptyDatabase()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ExpensesDbContext>()
            .UseSqlite(connection)
            .Options;

        using var db = new ExpensesDbContext(options);
        db.Database.Migrate();

        db.Database.GetPendingMigrations().Should().BeEmpty();
        db.Users.Count().Should().Be(0);
    }
}
