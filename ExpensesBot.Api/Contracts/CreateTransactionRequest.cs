namespace ExpensesBot.Api.Contracts.Transactions;

public enum TransactionType
{
    Expense = 0,
    Income = 1
}

public sealed record CreateTransactionRequest(
    long AmountMinor,
    string Currency,
    long? CategoryId,
    DateTime? OccurredAtUtc,
    string? Note,
    TransactionType Type = TransactionType.Expense,
    List<long>? TagIds = null,
    long? WalletId = null);

/// <param name="OccurredAtUtc">New date, or null to keep it.</param>
/// <param name="TagIds">The complete new set of tags, or null to keep them.</param>
public sealed record UpdateTransactionRequest(
    long AmountMinor,
    string Currency,
    long? CategoryId,
    string? Note,
    TransactionType? Type = null,
    DateTime? OccurredAtUtc = null,
    List<long>? TagIds = null);