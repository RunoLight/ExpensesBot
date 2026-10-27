namespace ExpensesBot.Api.Contracts.Transactions;

public enum TransactionType
{
    Expense = 0,
    Income = 1
}

/// <summary>Filters and order of the transaction history; every filter is optional.</summary>
/// <param name="Search">Case-insensitive text in the note or the category name.</param>
/// <param name="TagIds">Transactions with at least one of these tags.</param>
/// <param name="Sort">date-desc (default), date-asc, amount-desc, amount-asc, name-asc, name-desc.</param>
public sealed record TransactionQuery(
    string? Search = null,
    long? CategoryId = null,
    IReadOnlyCollection<long>? TagIds = null,
    string? Sort = null);

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