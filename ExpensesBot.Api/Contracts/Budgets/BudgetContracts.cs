namespace ExpensesBot.Api.Contracts.Budgets;

public sealed record BudgetResponse(
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

public sealed record CreateBudgetRequest(
    long AmountMinor,
    string Currency,
    long? CategoryId,
    DateTime StartDateUtc,
    DateTime EndDateUtc);

public sealed record UpdateBudgetRequest(
    long AmountMinor,
    string Currency,
    long? CategoryId,
    DateTime StartDateUtc,
    DateTime EndDateUtc);
