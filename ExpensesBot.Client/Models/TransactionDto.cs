namespace ExpensesBot.Client.Models;

public enum TransactionType
{
    Expense = 0,
    Income = 1
}

public sealed record TransactionDto(
    long Id,
    long AmountMinor,
    string Currency,
    long? CategoryId,
    string? CategoryName,
    DateTime OccurredAtUtc,
    DateTime CreatedAtUtc,
    string? Note,
    TransactionType Type,
    List<TransactionTagDto>? TransactionTags = null);
