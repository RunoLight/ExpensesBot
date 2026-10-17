namespace ExpensesBot.Client.Models;

public sealed record CategoryTotalDto(
    string CategoryName,
    long CategoryId,
    string Currency,
    long TotalAmountMinor,
    int TransactionCount);

public sealed record PeriodTotalDto(
    string Period,
    string Currency,
    long TotalAmountMinor,
    int TransactionCount);

/// <summary>
/// Summary returned by the API. Every amount except <see cref="TotalsByCurrency"/>
/// is in minor units of <see cref="Currency"/>.
/// </summary>
public sealed record UserSummaryDto(
    List<CategoryTotalDto> ByCategory,
    List<PeriodTotalDto> ByPeriod,
    Dictionary<string, long> TotalsByCurrency,
    long TotalTransactions,
    double AveragePerDay,
    double ProjectedMonthly,
    string Mode,
    int DaysInPeriod,
    long TotalIncomeMinor = 0,
    long TotalExpenseMinor = 0,
    long BalanceMinor = 0,
    string Currency = "AMD",
    bool UsesFallbackRates = false)
{
    public static UserSummaryDto Empty(string currency, string mode = "all") =>
        new([], [], new Dictionary<string, long>(), 0, 0, 0, mode, 0, Currency: currency);
}
