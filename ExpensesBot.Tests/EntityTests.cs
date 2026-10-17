using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Tests;

public class DbContextTests
{
    private ExpensesDbContext CreateDbContext(string? databaseName = null)
    {
        var name = databaseName ?? $"TestDatabase_{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<ExpensesDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ExpensesDbContext(options);
    }

    [Fact]
    public void OnModelCreating_ConfiguresAllEntities()
    {
        using var db = CreateDbContext();
        db.Database.EnsureCreated();

        // Verify all entity types are registered in the model
        var model = db.Model;
        var entityNames = model.GetEntityTypes().Select(e => e.ClrType.Name).ToHashSet();

        entityNames.Should().Contain("User");
        entityNames.Should().Contain("Transaction");
        entityNames.Should().Contain("Category");
        entityNames.Should().Contain("Budget");
        entityNames.Should().Contain("RecurringTransaction");
        entityNames.Should().Contain("Goal");
        entityNames.Should().Contain("Tag");
        entityNames.Should().Contain("TransactionTag");
        entityNames.Should().Contain("ExchangeRate");
    }

    [Fact]
    public void User_CanHaveMultipleTransactions()
    {
        using var db = CreateDbContext();
        var user = new User { TelegramUserId = 123, DisplayName = "Test" };
        db.Users.Add(user);
        db.SaveChanges();

        var tx1 = new Transaction
        {
            UserId = user.Id,
            AmountMinor = 10000,
            Currency = "AMD",
            Type = TransactionType.Expense,
            OccurredAtUtc = DateTime.UtcNow
        };
        var tx2 = new Transaction
        {
            UserId = user.Id,
            AmountMinor = 20000,
            Currency = "USD",
            Type = TransactionType.Income,
            OccurredAtUtc = DateTime.UtcNow
        };
        db.Transactions.AddRange(tx1, tx2);
        db.SaveChanges();

        var userTransactions = db.Transactions.Where(t => t.UserId == user.Id).ToList();
        userTransactions.Count.Should().Be(2);
    }

    [Fact]
    public void TransactionTag_IsManyToMany()
    {
        using var db = CreateDbContext();
        var user = new User { TelegramUserId = 456, DisplayName = "TagTest" };
        db.Users.Add(user);
        db.SaveChanges();

        var tag = new Tag { UserId = user.Id, Name = "Vacation", Color = "#FF6384" };
        db.Tags.Add(tag);
        db.SaveChanges();

        var transaction = new Transaction
        {
            UserId = user.Id,
            AmountMinor = 50000,
            Currency = "RUB",
            Type = TransactionType.Expense,
            OccurredAtUtc = DateTime.UtcNow
        };
        db.Transactions.Add(transaction);
        db.SaveChanges();

        var transactionTag = new TransactionTag
        {
            TransactionId = transaction.Id,
            TagId = tag.Id
        };
        db.TransactionTags.Add(transactionTag);
        db.SaveChanges();

        var txWithTags = db.Transactions
            .Include(t => t.TransactionTags)
            .ThenInclude(tt => tt.Tag)
            .FirstOrDefault(t => t.Id == transaction.Id);

        txWithTags.Should().NotBeNull();
        txWithTags!.TransactionTags.Should().ContainSingle();
        txWithTags.TransactionTags[0].Tag.Name.Should().Be("Vacation");
    }

    [Fact]
    public void Category_CanBeArchived()
    {
        using var db = CreateDbContext();
        var user = new User { TelegramUserId = 789, DisplayName = "ArchiveTest" };
        db.Users.Add(user);
        db.SaveChanges();

        var category = new Category
        {
            UserId = user.Id,
            Name = "Food",
            Icon = "🍔",
            IsArchived = false
        };
        db.Categories.Add(category);
        db.SaveChanges();

        category.IsArchived = true;
        db.SaveChanges();

        var archived = db.Categories.FirstOrDefault(c => c.Id == category.Id);
        archived!.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Budget_HasDateRange()
    {
        using var db = CreateDbContext();
        var user = new User { TelegramUserId = 999, DisplayName = "BudgetTest" };
        db.Users.Add(user);
        db.SaveChanges();

        var budget = new Budget
        {
            UserId = user.Id,
            AmountMinor = 100000,
            Currency = "AMD",
            StartDateUtc = DateTime.UtcNow.AddDays(-10),
            EndDateUtc = DateTime.UtcNow.AddDays(20),
            IsActive = true
        };
        db.Budgets.Add(budget);
        db.SaveChanges();

        var stored = db.Budgets.FirstOrDefault(b => b.Id == budget.Id);
        stored.Should().NotBeNull();
        stored!.StartDateUtc.Date.Should().Be(budget.StartDateUtc.Date);
        stored.EndDateUtc.Date.Should().Be(budget.EndDateUtc.Date);
    }

    [Fact]
    public void Goal_CanHaveTargetDate()
    {
        using var db = CreateDbContext();
        var user = new User { TelegramUserId = 111, DisplayName = "GoalTest" };
        db.Users.Add(user);
        db.SaveChanges();

        var goal = new Goal
        {
            UserId = user.Id,
            Name = "New Phone",
            TargetAmountMinor = 5000000,
            Currency = "AMD",
            TargetDateUtc = DateTime.UtcNow.AddMonths(3),
            IsActive = true
        };
        db.Goals.Add(goal);
        db.SaveChanges();

        var stored = db.Goals.FirstOrDefault(g => g.Id == goal.Id);
        stored.Should().NotBeNull();
        stored!.TargetDateUtc.Value.Date.Should().Be(goal.TargetDateUtc.Value.Date);
    }

    [Fact]
    public void RecurringTransaction_HasPattern()
    {
        using var db = CreateDbContext();
        var user = new User { TelegramUserId = 222, DisplayName = "RecurringTest" };
        db.Users.Add(user);
        db.SaveChanges();

        var recurring = new RecurringTransaction
        {
            UserId = user.Id,
            AmountMinor = 100000,
            Currency = "RUB",
            Pattern = RecurrencePattern.Monthly,
            IsActive = true
        };
        db.RecurringTransactions.Add(recurring);
        db.SaveChanges();

        var stored = db.RecurringTransactions.FirstOrDefault(r => r.Id == recurring.Id);
        stored.Should().NotBeNull();
        stored!.Pattern.Should().Be(RecurrencePattern.Monthly);
    }

    [Fact]
    public void ExchangeRate_StoresRate()
    {
        using var db = CreateDbContext();
        var rate = new ExchangeRate
        {
            FromCurrency = "USD",
            ToCurrency = "RUB",
            Rate = 90.5m,
            FetchedAtUtc = DateTime.UtcNow
        };
        db.ExchangeRates.Add(rate);
        db.SaveChanges();

        var stored = db.ExchangeRates.FirstOrDefault(r =>
            r.FromCurrency == "USD" && r.ToCurrency == "RUB");
        stored.Should().NotBeNull();
        stored!.Rate.Should().Be(90.5m);
    }
}
