using System.Globalization;
using ExpensesBot.Api.Contracts.Summary;
using ExpensesBot.Api.Services;
using ExpensesBot.Data;
using Microsoft.EntityFrameworkCore;
using EntityTransactionType = ExpensesBot.Data.Entities.TransactionType;

namespace ExpensesBot.Api.Application;

/// <summary>
/// Income/expense summary. Every money value is converted into one display
/// currency, so AMD, USD and RUB transactions can be added up meaningfully.
/// </summary>
public sealed class SummaryService(ExpensesDbContext db, ExchangeRateProvider exchangeRates, TimeProvider clock)
{
    public static readonly IReadOnlyList<string> Modes = ["all", "30days", "month"];

    public async Task<UserSummaryResponse> GetAsync(
        long userId,
        string? mode,
        string? currency,
        DateTime? from,
        DateTime? to,
        CancellationToken ct)
    {
        var target = Currencies.TryNormalize(currency, out var requested)
            ? requested
            : await db.Users
                .Where(x => x.Id == userId)
                .Select(x => x.DefaultCurrency)
                .FirstAsync(ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var summaryMode = mode?.ToLowerInvariant() is { } m && Modes.Contains(m) ? m : "all";

        var fromUtc = Rules.ToUtc(from);
        var toUtc = Rules.ToUtc(to);
        var usesModeRange = fromUtc is null && toUtc is null;

        if (usesModeRange && summaryMode == "30days")
        {
            fromUtc = now.AddDays(-30);
            toUtc = now;
        }
        else if (usesModeRange && summaryMode == "month")
        {
            fromUtc = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            toUtc = now;
        }

        var query = db.Transactions.AsNoTracking().Where(x => x.UserId == userId);

        if (fromUtc.HasValue)
            query = query.Where(x => x.OccurredAtUtc >= fromUtc.Value);

        if (toUtc.HasValue)
            query = query.Where(x => x.OccurredAtUtc <= toUtc.Value);

        // One query; grouping happens in memory because amounts must be
        // converted with exchange rates first.
        var rows = await query
            .Select(x => new Row(
                x.CategoryId,
                x.Category != null ? x.Category.Name : null,
                x.Currency,
                x.Type,
                x.AmountMinor,
                x.OccurredAtUtc))
            .ToListAsync(ct);

        var rates = await exchangeRates.GetAsync(ct);
        var text = await db.TextForAsync(userId, ct);
        long ToTarget(Row row) => rates.Convert(row.AmountMinor, row.Currency, target) ?? 0;

        var expenses = rows.Where(x => x.Type == EntityTransactionType.Expense).ToList();
        var incomeMinor = rows.Where(x => x.Type == EntityTransactionType.Income).Sum(ToTarget);
        var expenseMinor = expenses.Sum(ToTarget);

        var byCategory = expenses
            .GroupBy(x => new { x.CategoryId, x.CategoryName })
            .Select(g => new CategoryBreakdown(
                g.Key.CategoryName ?? text.NoCategory,
                g.Key.CategoryId ?? 0,
                target,
                g.Sum(ToTarget),
                g.Count()))
            .OrderByDescending(x => x.TotalAmountMinor)
            .ToList();

        var byPeriod = expenses
            .GroupBy(x => x.OccurredAtUtc.ToString("yyyy-MM", CultureInfo.InvariantCulture))
            .Select(g => new PeriodBreakdown(g.Key, target, g.Sum(ToTarget), g.Count()))
            .OrderByDescending(x => x.Period, StringComparer.Ordinal)
            .ToList();

        // Spending per original currency, without conversion.
        var expensesByCurrency = expenses
            .GroupBy(x => x.Currency)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.AmountMinor));

        var (averagePerDay, projected, days) = Project(
            summaryMode, usesModeRange, fromUtc, toUtc, now, expenseMinor,
            () => expenses.Where(x => x.OccurredAtUtc >= now.AddDays(-30)).Sum(ToTarget));

        return new UserSummaryResponse(
            byCategory,
            byPeriod,
            expensesByCurrency,
            rows.Count,
            Math.Round(averagePerDay),
            Math.Round(projected),
            summaryMode,
            days,
            incomeMinor,
            expenseMinor,
            incomeMinor - expenseMinor,
            target,
            rates.IsFallback);
    }

    /// <summary>Average daily spending and a projection, in minor units.</summary>
    internal static (double AveragePerDay, double Projected, int DaysInPeriod) Project(
        string mode,
        bool usesModeRange,
        DateTime? fromUtc,
        DateTime? toUtc,
        DateTime now,
        long expenseMinor,
        Func<long> lastThirtyDaysExpense)
    {
        if (usesModeRange && mode == "month")
        {
            // Spending so far this month, extrapolated to the whole month.
            var elapsed = Math.Max(1.0, (now - fromUtc!.Value).TotalDays);
            var average = expenseMinor / elapsed;
            return (average, average * DateTime.DaysInMonth(now.Year, now.Month), (int)Math.Ceiling(elapsed));
        }

        if (fromUtc.HasValue && toUtc.HasValue && toUtc > fromUtc)
        {
            var days = Math.Max(1.0, (toUtc.Value - fromUtc.Value).TotalDays);
            var average = expenseMinor / days;
            return (average, average * 30, (int)Math.Ceiling(days));
        }

        // All time: the last 30 days are the best basis for a monthly estimate.
        var recent = lastThirtyDaysExpense();
        return (recent / 30.0, recent, 0);
    }

    private sealed record Row(
        long? CategoryId,
        string? CategoryName,
        string Currency,
        EntityTransactionType Type,
        long AmountMinor,
        DateTime OccurredAtUtc);
}
