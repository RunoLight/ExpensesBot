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

public sealed record UpdateTransactionRequest(
    long AmountMinor,
    string Currency,
    long? CategoryId,
    string? Note,
    TransactionType? Type = null);