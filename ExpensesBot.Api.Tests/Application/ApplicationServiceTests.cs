using ExpensesBot.Api.Application;
using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.Budgets;
using ExpensesBot.Api.Contracts.Goals;
using ExpensesBot.Api.Services;
using ExpensesBot.Api.Tests.Infrastructure;
using ExpensesBot.Data.Entities;
using FluentAssertions;

namespace ExpensesBot.Api.Tests.Application;

public class SummaryServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private readonly TestDatabase _database = new();

    private static Transaction Expense(long userId, long amountMinor, DateTime occurredAt, string currency = "AMD") => new()
    {
        UserId = userId,
        AmountMinor = amountMinor,
        Currency = currency,
        Type = TransactionType.Expense,
        OccurredAtUtc = occurredAt
    };

    private SummaryService CreateService()
    {
        var db = _database.CreateContext();
        return new SummaryService(db, new ExchangeRateProvider(db, new FixedTimeProvider(Now)), new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task MonthMode_ProjectsSpendingSoFarToTheWholeMonth()
    {
        var user = await _database.AddUserAsync();
        await _database.AddAsync(
            Expense(user.Id, 150_000, new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc)),
            Expense(user.Id, 150_000, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc)),
            Expense(user.Id, 999_999, new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc))); // last month

        var summary = await CreateService().GetAsync(user.Id, "month", "AMD", null, null, default);

        // 3 000 AMD in 15.5 days → ~193.55 AMD/day → ~6 000 AMD over 31 days.
        summary.TotalExpenseMinor.Should().Be(300_000);
        summary.AveragePerDay.Should().BeApproximately(19_355, 1);
        summary.ProjectedMonthly.Should().BeApproximately(600_000, 31);
        summary.DaysInPeriod.Should().Be(16);
    }

    [Fact]
    public async Task OtherUsersTransactionsAreIgnored()
    {
        var user = await _database.AddUserAsync(1);
        var other = await _database.AddUserAsync(2);
        await _database.AddAsync(
            Expense(user.Id, 10_000, Now.AddDays(-1)),
            Expense(other.Id, 90_000, Now.AddDays(-1)));

        var summary = await CreateService().GetAsync(user.Id, "all", "AMD", null, null, default);

        summary.TotalExpenseMinor.Should().Be(10_000);
        summary.TotalTransactions.Should().Be(1);
    }

    [Fact]
    public async Task WithoutCurrency_UsesTheUsersDefaultCurrency()
    {
        var user = await _database.AddUserAsync();

        var summary = await CreateService().GetAsync(user.Id, null, null, null, null, default);

        summary.Currency.Should().Be("AMD");
        summary.Mode.Should().Be("all");
    }

    public void Dispose() => _database.Dispose();
}

public class BudgetServiceTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly TestDatabase _database = new();

    [Fact]
    public async Task SpentAmount_CountsOnlyMatchingExpenses()
    {
        var user = await _database.AddUserAsync(1);
        var other = await _database.AddUserAsync(2);
        var food = new Category { UserId = user.Id, Name = "Food" };
        await _database.AddAsync(food);

        await _database.AddAsync(
            new Budget { UserId = user.Id, CategoryId = food.Id, AmountMinor = 100_000, Currency = "AMD", StartDateUtc = Start, EndDateUtc = Start.AddMonths(1) },
            new Budget { UserId = user.Id, AmountMinor = 1_000_000, Currency = "AMD", StartDateUtc = Start, EndDateUtc = Start.AddMonths(1) },
            new Transaction { UserId = user.Id, CategoryId = food.Id, AmountMinor = 25_000, Currency = "AMD", OccurredAtUtc = Start.AddDays(3) },
            new Transaction { UserId = user.Id, AmountMinor = 50_000, Currency = "AMD", OccurredAtUtc = Start.AddDays(4) },
            new Transaction { UserId = user.Id, CategoryId = food.Id, AmountMinor = 7_000, Currency = "USD", OccurredAtUtc = Start.AddDays(5) },
            new Transaction { UserId = user.Id, CategoryId = food.Id, AmountMinor = 40_000, Currency = "AMD", OccurredAtUtc = Start.AddDays(-1) },
            new Transaction { UserId = user.Id, AmountMinor = 80_000, Currency = "AMD", Type = TransactionType.Income, OccurredAtUtc = Start.AddDays(6) },
            new Transaction { UserId = other.Id, AmountMinor = 99_000, Currency = "AMD", OccurredAtUtc = Start.AddDays(6) });

        await using var db = _database.CreateContext();
        var budgets = await new BudgetService(db).GetActiveAsync(user.Id, default);

        var foodBudget = budgets.Single(b => b.CategoryId == food.Id);
        var overall = budgets.Single(b => b.CategoryId == null);

        foodBudget.SpentAmountMinor.Should().Be(25_000, "other currencies, dates and categories don't count");
        foodBudget.ProgressPercent.Should().Be(25);
        overall.SpentAmountMinor.Should().Be(75_000, "income and other users don't count");
    }

    [Fact]
    public async Task Create_RejectsEndBeforeStart()
    {
        var user = await _database.AddUserAsync();
        await using var db = _database.CreateContext();

        var result = await new BudgetService(db).CreateAsync(user.Id,
            new CreateBudgetRequest(1_000, "AMD", null, Start, Start.AddDays(-1)), default);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(ErrorKind.Validation);
    }

    public void Dispose() => _database.Dispose();
}

public class TransactionServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();

    [Fact]
    public async Task GetPage_WithTag_ReturnsOnlyTaggedTransactions()
    {
        var user = await _database.AddUserAsync();
        var vacation = new Tag { UserId = user.Id, Name = "отпуск" };
        await _database.AddAsync(vacation);

        var tagged = new Transaction { UserId = user.Id, AmountMinor = 1_000, Currency = "AMD", Note = "Hotel" };
        tagged.TransactionTags.Add(new TransactionTag { TagId = vacation.Id });
        await _database.AddAsync(tagged,
            new Transaction { UserId = user.Id, AmountMinor = 2_000, Currency = "AMD", Note = "Groceries" });

        await using var db = _database.CreateContext();
        var service = new TransactionService(db, TimeProvider.System);

        (await service.GetPageAsync(user.Id, 50, 0, vacation.Id, default))
            .Should().ContainSingle().Which.Note.Should().Be("Hotel");

        (await service.GetPageAsync(user.Id, 50, 0, null, default)).Should().HaveCount(2);
    }

    public void Dispose() => _database.Dispose();
}

public class GoalServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();

    [Fact]
    public async Task Progress_CountsContributionsButNotOtherIncome()
    {
        var user = await _database.AddUserAsync();
        await using var db = _database.CreateContext();
        var service = new GoalService(db, TimeProvider.System);

        var goal = (await service.CreateAsync(user.Id,
            new CreateGoalRequest("Ноутбук", 500_000, "AMD", null, null), default)).Value!;

        await _database.AddAsync(new Transaction
        {
            UserId = user.Id, AmountMinor = 2_000_000, Currency = "AMD", Type = TransactionType.Income
        });

        (await service.ContributeAsync(user.Id, goal.Id, new ContributeToGoalRequest(100_000), default))
            .IsSuccess.Should().BeTrue();

        var reloaded = (await service.GetAllAsync(user.Id, default)).Single();

        reloaded.SavedAmountMinor.Should().Be(100_000);
        reloaded.ProgressPercent.Should().Be(20);
    }

    [Fact]
    public async Task Contribute_ToAnotherUsersGoal_IsNotFound()
    {
        var owner = await _database.AddUserAsync(1);
        var stranger = await _database.AddUserAsync(2);
        await using var db = _database.CreateContext();
        var service = new GoalService(db, TimeProvider.System);

        var goal = (await service.CreateAsync(owner.Id,
            new CreateGoalRequest("Велосипед", 100_000, "AMD", null, null), default)).Value!;

        var result = await service.ContributeAsync(stranger.Id, goal.Id, new ContributeToGoalRequest(1_000), default);

        result.Error!.Kind.Should().Be(ErrorKind.NotFound);
    }

    public void Dispose() => _database.Dispose();
}
