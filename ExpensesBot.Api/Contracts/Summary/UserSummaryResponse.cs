namespace ExpensesBot.Api.Contracts.Summary;

/// <summary>Expenses of one category, converted to the summary currency.</summary>
public sealed record CategoryBreakdown(
    string CategoryName,
    long CategoryId,
    string Currency,
    long TotalAmountMinor,
    int TransactionCount);

/// <summary>Expenses of one month (yyyy-MM), converted to the summary currency.</summary>
public sealed record PeriodBreakdown(
    string Period,
    string Currency,
    long TotalAmountMinor,
    int TransactionCount);

/// <param name="TotalsByCurrency">Expenses per original currency, not converted.</param>
/// <param name="AveragePerDay">Average daily expense, minor units of <paramref name="Currency"/>.</param>
/// <param name="ProjectedMonthly">Projected monthly expense, minor units of <paramref name="Currency"/>.</param>
/// <param name="Currency">Currency all converted values are expressed in.</param>
/// <param name="UsesFallbackRates">True when no fresh exchange rates were available.</param>
public sealed record UserSummaryResponse(
    List<CategoryBreakdown> ByCategory,
    List<PeriodBreakdown> ByPeriod,
    Dictionary<string, long> TotalsByCurrency,
    long TotalTransactions,
    double AveragePerDay,
    double ProjectedMonthly,
    string Mode,
    int DaysInPeriod,
    long TotalIncomeMinor,
    long TotalExpenseMinor,
    long BalanceMinor,
    string Currency,
    bool UsesFallbackRates);
