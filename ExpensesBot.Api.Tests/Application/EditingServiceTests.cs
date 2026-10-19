using ExpensesBot.Api.Application;
using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.Budgets;
using ExpensesBot.Api.Contracts.Goals;
using ExpensesBot.Api.Contracts.Tags;
using ExpensesBot.Api.Tests.Infrastructure;
using ExpensesBot.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Api.Tests.Application;

public class BudgetEditingTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly TestDatabase _database = new();

    private async Task<(User User, Budget Budget)> SeedAsync()
    {
        var user = await _database.AddUserAsync();
        var budget = new Budget
        {
            UserId = user.Id,
            AmountMinor = 100_000,
            Currency = "AMD",
            StartDateUtc = Start,
            EndDateUtc = Start.AddMonths(1)
        };
        await _database.AddAsync(budget,
            new Transaction { UserId = user.Id, AmountMinor = 30_000, Currency = "AMD", OccurredAtUtc = Start.AddDays(2) });
        return (user, budget);
    }

    [Fact]
    public async Task Update_ChangesLimitAndRecalculatesProgress()
    {
        var (user, budget) = await SeedAsync();
        await using var db = _database.CreateContext();

        var result = await new BudgetService(db).UpdateAsync(user.Id, budget.Id,
            new UpdateBudgetRequest(60_000, "amd", null, Start, Start.AddMonths(1)), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AmountMinor.Should().Be(60_000);
        result.Value.Currency.Should().Be("AMD");
        result.Value.SpentAmountMinor.Should().Be(30_000);
        result.Value.ProgressPercent.Should().Be(50);
    }

    [Fact]
    public async Task Update_AnotherUsersBudget_IsNotFound()
    {
        var (_, budget) = await SeedAsync();
        var stranger = await _database.AddUserAsync(2);
        await using var db = _database.CreateContext();

        var result = await new BudgetService(db).UpdateAsync(stranger.Id, budget.Id,
            new UpdateBudgetRequest(60_000, "AMD", null, Start, Start.AddMonths(1)), default);

        result.Error!.Kind.Should().Be(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Update_NewLimit_ReArmsTheBudgetAlert()
    {
        var (user, budget) = await SeedAsync();
        await _database.AddAsync(new NotificationLog
        {
            UserId = user.Id,
            Kind = NotificationKind.BudgetAlert,
            Key = NotificationLog.BudgetKey(budget.Id),
            SentAtUtc = Start.AddDays(3)
        });

        await using var db = _database.CreateContext();
        await new BudgetService(db).UpdateAsync(user.Id, budget.Id,
            new UpdateBudgetRequest(200_000, "AMD", null, Start, Start.AddMonths(1)), default);

        (await db.NotificationLogs.AnyAsync()).Should().BeFalse("a raised limit should alert again at 90%");
    }

    [Fact]
    public async Task Update_WithoutChanges_KeepsTheAlertState()
    {
        var (user, budget) = await SeedAsync();
        await _database.AddAsync(new NotificationLog
        {
            UserId = user.Id,
            Kind = NotificationKind.BudgetAlert,
            Key = NotificationLog.BudgetKey(budget.Id),
            SentAtUtc = Start.AddDays(3)
        });

        await using var db = _database.CreateContext();
        await new BudgetService(db).UpdateAsync(user.Id, budget.Id,
            new UpdateBudgetRequest(100_000, "AMD", null, Start, Start.AddMonths(1)), default);

        (await db.NotificationLogs.CountAsync()).Should().Be(1);
    }

    public void Dispose() => _database.Dispose();
}

public class GoalEditingTests : IDisposable
{
    private readonly TestDatabase _database = new();

    [Fact]
    public async Task Update_RenamesAndKeepsProgress()
    {
        var user = await _database.AddUserAsync();
        await using var db = _database.CreateContext();
        var service = new GoalService(db, TimeProvider.System);

        var goal = (await service.CreateAsync(user.Id, new CreateGoalRequest("Ноутбук", 500_000, "AMD", null, null), default)).Value!;
        await service.ContributeAsync(user.Id, goal.Id, new ContributeToGoalRequest(100_000), default);

        var result = await service.UpdateAsync(user.Id, goal.Id,
            new UpdateGoalRequest("Игровой ноутбук", 1_000_000, new DateTime(2027, 1, 1)), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Игровой ноутбук");
        result.Value.Currency.Should().Be("AMD");
        result.Value.SavedAmountMinor.Should().Be(100_000);
        result.Value.ProgressPercent.Should().Be(10);
        result.Value.TargetDateUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Update_LegacyGoal_CarriesContributionsOverToTheNewName()
    {
        // Goals created before goal categories match contributions by note.
        var user = await _database.AddUserAsync();
        var goal = new Goal { UserId = user.Id, Name = "Отпуск", TargetAmountMinor = 100_000, Currency = "AMD" };
        await _database.AddAsync(goal, new Transaction
        {
            UserId = user.Id,
            AmountMinor = 40_000,
            Currency = "AMD",
            Type = TransactionType.Income,
            Note = "Вклад в цель: Отпуск"
        });

        await using var db = _database.CreateContext();
        var result = await new GoalService(db, TimeProvider.System).UpdateAsync(user.Id, goal.Id,
            new UpdateGoalRequest("Отпуск в Грузии", 100_000, null), default);

        result.Value!.SavedAmountMinor.Should().Be(40_000);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Update_BlankName_IsRejected(string name)
    {
        var user = await _database.AddUserAsync();
        await using var db = _database.CreateContext();
        var service = new GoalService(db, TimeProvider.System);
        var goal = (await service.CreateAsync(user.Id, new CreateGoalRequest("Велосипед", 50_000, "AMD", null, null), default)).Value!;

        var result = await service.UpdateAsync(user.Id, goal.Id, new UpdateGoalRequest(name, 50_000, null), default);

        result.Error!.Kind.Should().Be(ErrorKind.Validation);
    }

    public void Dispose() => _database.Dispose();
}

public class TagEditingTests : IDisposable
{
    private readonly TestDatabase _database = new();

    [Fact]
    public async Task Update_RenamesAndRecolours()
    {
        var user = await _database.AddUserAsync();
        await using var db = _database.CreateContext();
        var service = new TagService(db);
        var tag = (await service.CreateAsync(user.Id, new CreateTagRequest("отпуск", "#36A2EB"), default)).Value!;

        var result = await service.UpdateAsync(user.Id, tag.Id, new UpdateTagRequest(" Отпуск 2026 ", "#ff6384"), default);

        result.Value!.Name.Should().Be("Отпуск 2026");
        result.Value.Color.Should().Be("#FF6384");
    }

    [Fact]
    public async Task Update_InvalidColour_KeepsTheOldOne()
    {
        var user = await _database.AddUserAsync();
        await using var db = _database.CreateContext();
        var service = new TagService(db);
        var tag = (await service.CreateAsync(user.Id, new CreateTagRequest("еда", "#4BC0C0"), default)).Value!;

        var result = await service.UpdateAsync(user.Id, tag.Id, new UpdateTagRequest("еда", "red"), default);

        result.Value!.Color.Should().Be("#4BC0C0");
    }

    [Fact]
    public async Task Update_ToAnotherTagsName_IsAConflict_ButChangingCaseIsFine()
    {
        var user = await _database.AddUserAsync();
        await using var db = _database.CreateContext();
        var service = new TagService(db);
        await service.CreateAsync(user.Id, new CreateTagRequest("работа", "#36A2EB"), default);
        var tag = (await service.CreateAsync(user.Id, new CreateTagRequest("отпуск", "#36A2EB"), default)).Value!;

        (await service.UpdateAsync(user.Id, tag.Id, new UpdateTagRequest("Работа", "#36A2EB"), default))
            .Error!.Kind.Should().Be(ErrorKind.Conflict);

        (await service.UpdateAsync(user.Id, tag.Id, new UpdateTagRequest("Отпуск", "#36A2EB"), default))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Update_AnotherUsersTag_IsNotFound()
    {
        var owner = await _database.AddUserAsync(1);
        var stranger = await _database.AddUserAsync(2);
        await using var db = _database.CreateContext();
        var service = new TagService(db);
        var tag = (await service.CreateAsync(owner.Id, new CreateTagRequest("отпуск", "#36A2EB"), default)).Value!;

        (await service.UpdateAsync(stranger.Id, tag.Id, new UpdateTagRequest("чужой", "#36A2EB"), default))
            .Error!.Kind.Should().Be(ErrorKind.NotFound);
    }

    public void Dispose() => _database.Dispose();
}
