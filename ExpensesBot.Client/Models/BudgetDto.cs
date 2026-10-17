namespace ExpensesBot.Client.Models;

public sealed record BudgetDto(
    long Id,
    long AmountMinor,
    string Currency,
    long? CategoryId,
    string? CategoryName,
    string? CategoryIcon,
    DateTime StartDateUtc,
    DateTime EndDateUtc,
    bool IsActive,
    long SpentAmountMinor,
    double ProgressPercent);
