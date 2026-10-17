using ExpensesBot.Api.Common;
using ExpensesBot.Api.Contracts.Budgets;
using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpensesBot.Api.Application;

public sealed class BudgetService(ExpensesDbContext db)
{
    /// <summary>Active budgets with spent amount. Uses two queries regardless of the number of budgets.</summary>
    public async Task<List<BudgetResponse>> GetActiveAsync(long userId, CancellationToken ct)
    {
        var budgets = await db.Budgets
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive)
            .OrderByDescending(x => x.EndDateUtc)
            .Select(x => new
            {
                x.Id,
                x.AmountMinor,
                x.Currency,
                x.CategoryId,
                CategoryName = x.Category != null ? x.Category.Name : null,
                CategoryIcon = x.Category != null ? x.Category.Icon : null,
                x.StartDateUtc,
                x.EndDateUtc,
                x.IsActive
            })
            .ToListAsync(ct);

        if (budgets.Count == 0)
            return [];

        var from = budgets.Min(x => x.StartDateUtc);
        var to = budgets.Max(x => x.EndDateUtc);
        var currencies = budgets.Select(x => x.Currency).Distinct().ToList();

        var expenses = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId &&
                        t.Type == TransactionType.Expense &&
                        t.OccurredAtUtc >= from &&
                        t.OccurredAtUtc <= to &&
                        currencies.Contains(t.Currency))
            .Select(t => new { t.CategoryId, t.Currency, t.OccurredAtUtc, t.AmountMinor })
            .ToListAsync(ct);

        return budgets
            .Select(b =>
            {
                var spent = expenses
                    .Where(t => t.Currency == b.Currency &&
                                t.OccurredAtUtc >= b.StartDateUtc &&
                                t.OccurredAtUtc <= b.EndDateUtc &&
                                (b.CategoryId == null || t.CategoryId == b.CategoryId))
                    .Sum(t => t.AmountMinor);

                return new BudgetResponse(
                    b.Id,
                    b.AmountMinor,
                    b.Currency,
                    b.CategoryId,
                    b.CategoryName,
                    b.CategoryIcon,
                    b.StartDateUtc,
                    b.EndDateUtc,
                    b.IsActive,
                    spent,
                    ProgressMath.Percent(spent, b.AmountMinor));
            })
            .ToList();
    }

    public async Task<Result<BudgetResponse>> CreateAsync(long userId, CreateBudgetRequest request, CancellationToken ct)
    {
        if (Rules.ValidateAmount(request.AmountMinor) is { } amountError)
            return amountError;

        if (request.EndDateUtc <= request.StartDateUtc)
            return Error.Validation("End date must be after start date.");

        if (Rules.ValidateCurrency(request.Currency, out var currency) is { } currencyError)
            return currencyError;

        Category? category = null;
        if (request.CategoryId.HasValue)
        {
            category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(
                x => x.Id == request.CategoryId.Value && x.UserId == userId && !x.IsArchived, ct);

            if (category is null)
                return Error.Validation("Category not found.");
        }

        var budget = new Budget
        {
            UserId = userId,
            CategoryId = category?.Id,
            AmountMinor = request.AmountMinor,
            Currency = currency,
            StartDateUtc = DateTime.SpecifyKind(request.StartDateUtc, DateTimeKind.Utc),
            EndDateUtc = DateTime.SpecifyKind(request.EndDateUtc, DateTimeKind.Utc),
            IsActive = true
        };

        db.Budgets.Add(budget);
        await db.SaveChangesAsync(ct);

        return new BudgetResponse(
            budget.Id, budget.AmountMinor, budget.Currency, budget.CategoryId,
            category?.Name, category?.Icon, budget.StartDateUtc, budget.EndDateUtc,
            budget.IsActive, 0, 0);
    }

    public async Task<Result> DeleteAsync(long userId, long id, CancellationToken ct)
    {
        var deleted = await db.Budgets
            .Where(x => x.Id == id && x.UserId == userId)
            .ExecuteDeleteAsync(ct);

        return deleted == 0 ? Error.NotFound() : Result.Success();
    }
}

public static class ProgressMath
{
    /// <summary>Share of <paramref name="part"/> in <paramref name="whole"/>, 0–100.</summary>
    public static double Percent(long part, long whole) =>
        whole > 0 ? Math.Clamp((double)part / whole * 100, 0, 100) : 0;
}
