using ExpensesBot.Api.Contracts.Transactions;

namespace ExpensesBot.Api.Contracts.RecurringTransactions;

public sealed record RecurringTransactionResponse(
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
    TransactionType Type);

public sealed record CreateRecurringTransactionRequest(
    long AmountMinor,
    string Currency,
    long? CategoryId,
    string? Note,
    string Pattern,
    DateTime? EndsAfter,
    TransactionType Type = TransactionType.Expense);

/// <param name="Pattern">New schedule, or null to keep it.</param>
/// <param name="Type">Expense or income, or null to keep it.</param>
public sealed record UpdateRecurringTransactionRequest(
    long AmountMinor,
    string Currency,
    long? CategoryId,
    string? Note,
    bool IsActive,
    string? Pattern = null,
    TransactionType? Type = null);
