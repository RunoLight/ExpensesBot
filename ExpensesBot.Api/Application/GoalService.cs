using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.Goals;
using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Api.Application;

/// <summary>
/// Savings goals. A goal is funded by income transactions in its own category
/// (created automatically with the goal), so ordinary income such as salary
/// does not count towards it.
/// </summary>
public sealed class GoalService(ExpensesDbContext db, TimeProvider clock)
{
    private const int MaxNameLength = 80;

    public async Task<List<GoalResponse>> GetAllAsync(long userId, CancellationToken ct)
    {
        var goals = await db.Goals
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.TargetAmountMinor,
                x.Currency,
                x.CategoryId,
                CategoryName = x.Category != null ? x.Category.Name : null,
                CategoryIcon = x.Category != null ? x.Category.Icon : null,
                x.TargetDateUtc,
                x.IsActive,
                x.CreatedAtUtc
            })
            .ToListAsync(ct);

        if (goals.Count == 0)
            return [];

        // Savings per goal category, in one query.
        var categoryIds = goals.Where(g => g.CategoryId.HasValue).Select(g => g.CategoryId!.Value).ToList();
        var savedByCategory = await db.Transactions
            .Where(t => t.UserId == userId &&
                        t.Type == TransactionType.Income &&
                        t.CategoryId != null &&
                        categoryIds.Contains(t.CategoryId.Value))
            .GroupBy(t => new { t.CategoryId, t.Currency })
            .Select(g => new { g.Key.CategoryId, g.Key.Currency, Total = g.Sum(t => t.AmountMinor) })
            .ToListAsync(ct);

        // Goals created before goals had their own category are matched by the
        // note the contribute endpoint writes.
        var legacyNotes = goals.Where(g => g.CategoryId is null).Select(g => LegacyContributionNote(g.Name)).ToList();
        var savedByNote = await db.Transactions
            .Where(t => t.UserId == userId &&
                        t.Type == TransactionType.Income &&
                        t.Note != null &&
                        legacyNotes.Contains(t.Note))
            .GroupBy(t => new { t.Note, t.Currency })
            .Select(g => new { g.Key.Note, g.Key.Currency, Total = g.Sum(t => t.AmountMinor) })
            .ToListAsync(ct);

        return goals
            .Select(g =>
            {
                var saved = g.CategoryId.HasValue
                    ? savedByCategory.Where(x => x.CategoryId == g.CategoryId && x.Currency == g.Currency).Sum(x => x.Total)
                    : savedByNote.Where(x => x.Note == LegacyContributionNote(g.Name) && x.Currency == g.Currency).Sum(x => x.Total);

                return new GoalResponse(
                    g.Id,
                    g.Name,
                    g.TargetAmountMinor,
                    g.Currency,
                    g.CategoryId,
                    g.CategoryName,
                    g.CategoryIcon,
                    g.TargetDateUtc,
                    saved,
                    ProgressMath.Percent(saved, g.TargetAmountMinor),
                    g.IsActive,
                    g.CreatedAtUtc);
            })
            .ToList();
    }

    public async Task<Result<GoalResponse>> CreateAsync(long userId, CreateGoalRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
            return Error.Validation($"Name must be 1-{MaxNameLength} characters.");

        if (Rules.ValidateAmount(request.TargetAmountMinor, "Target amount") is { } amountError)
            return amountError;

        if (Rules.ValidateCurrency(request.Currency, out var currency) is { } currencyError)
            return currencyError;

        Category? category;
        if (request.CategoryId.HasValue)
        {
            category = await db.Categories.FirstOrDefaultAsync(
                x => x.Id == request.CategoryId.Value && x.UserId == userId && !x.IsArchived, ct);

            if (category is null)
                return Error.Validation("Category not found.");
        }
        else
        {
            category = new Category { UserId = userId, Name = name, Icon = "🎯" };
            db.Categories.Add(category);
        }

        var goal = new Goal
        {
            UserId = userId,
            Name = name,
            TargetAmountMinor = request.TargetAmountMinor,
            Currency = currency,
            Category = category,
            TargetDateUtc = Rules.ToUtc(request.TargetDateUtc),
            IsActive = true,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime
        };

        db.Goals.Add(goal);
        await db.SaveChangesAsync(ct);

        return new GoalResponse(
            goal.Id, goal.Name, goal.TargetAmountMinor, goal.Currency, goal.CategoryId,
            category.Name, category.Icon, goal.TargetDateUtc, 0, 0, goal.IsActive, goal.CreatedAtUtc);
    }

    /// <summary>Renames the goal or changes its target. Currency and category stay as they are.</summary>
    public async Task<Result<GoalResponse>> UpdateAsync(long userId, long id, UpdateGoalRequest request, CancellationToken ct)
    {
        var goal = await db.Goals.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (goal is null)
            return Error.NotFound();

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
            return Error.Validation($"Name must be 1-{MaxNameLength} characters.");

        if (Rules.ValidateAmount(request.TargetAmountMinor, "Target amount") is { } amountError)
            return amountError;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (goal.CategoryId is null && name != goal.Name)
        {
            // Goals from before goal categories find their contributions by note,
            // so the notes have to follow the new name.
            var oldNote = LegacyContributionNote(goal.Name);
            var newNote = LegacyContributionNote(name);

            await db.Transactions
                .Where(t => t.UserId == userId && t.Type == TransactionType.Income && t.Note == oldNote)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Note, newNote), ct);
        }

        goal.Name = name;
        goal.TargetAmountMinor = request.TargetAmountMinor;
        goal.TargetDateUtc = Rules.ToUtc(request.TargetDateUtc);
        goal.IsActive = request.IsActive;

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return (await GetAllAsync(userId, ct)).Single(g => g.Id == id);
    }

    public async Task<Result> DeleteAsync(long userId, long id, CancellationToken ct)
    {
        var deleted = await db.Goals
            .Where(x => x.Id == id && x.UserId == userId)
            .ExecuteDeleteAsync(ct);

        return deleted == 0 ? Error.NotFound() : Result.Success();
    }

    /// <summary>Records a contribution as income in the goal's category.</summary>
    public async Task<Result<ContributionResponse>> ContributeAsync(
        long userId,
        long goalId,
        ContributeToGoalRequest request,
        CancellationToken ct)
    {
        var goal = await db.Goals
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == goalId && x.UserId == userId, ct);

        if (goal is null)
            return Error.NotFound();

        if (!goal.IsActive)
            return Error.Conflict("Goal is inactive.");

        if (Rules.ValidateAmount(request.AmountMinor, "Contribution") is { } amountError)
            return amountError;

        var now = clock.GetUtcNow().UtcDateTime;

        // A legacy goal finds its contributions by this exact note; others get it in the user's language.
        var note = goal.CategoryId is null
            ? LegacyContributionNote(goal.Name)
            : (await db.TextForAsync(userId, ct)).GoalContributionNote(goal.Name);

        var transaction = new Transaction
        {
            UserId = userId,
            CategoryId = goal.CategoryId,
            AmountMinor = request.AmountMinor,
            Currency = goal.Currency,
            Type = TransactionType.Income,
            Note = note,
            OccurredAtUtc = now,
            CreatedAtUtc = now
        };

        db.Transactions.Add(transaction);
        await db.SaveChangesAsync(ct);

        return new ContributionResponse(transaction.Id, request.AmountMinor);
    }

    /// <summary>
    /// The note that links contributions to goals created before goals had a category.
    /// It is stored data, so it keeps its original (Russian) wording in every language.
    /// </summary>
    private static string LegacyContributionNote(string goalName) => $"Вклад в цель: {goalName}";
}
