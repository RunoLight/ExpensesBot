namespace ExpensesBot.Client.Models;

public sealed record RecurringTransactionDto(
    long Id,
    long AmountMinor,
    string Currency,
    long? CategoryId,
    string? CategoryName,
    string? Note,
    string Pattern,
    DateTime? EndsAfter,
    int TimesExecuted,
    DateTime LastExecutedAtUtc,
    bool IsActive,
    DateTime CreatedAtUtc,
    TransactionType Type = TransactionType.Expense);
