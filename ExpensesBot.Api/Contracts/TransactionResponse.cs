namespace ExpensesBot.Api.Contracts.Transactions;

public sealed record TransactionTagResponse(
    long TransactionId,
    long TagId);

public sealed record TransactionResponse(
    long Id,
    long AmountMinor,
    string Currency,
    long? CategoryId,
    string? CategoryName,
    DateTime OccurredAtUtc,
    DateTime CreatedAtUtc,
    string? Note,
    TransactionType Type,
    List<TransactionTagResponse> TransactionTags);

public sealed record BulkError(
    int Index,
    string Error);

public sealed record BulkCreateResponse(
    List<TransactionResponse> Created,
    List<BulkError> Errors,
    int TotalCreated,
    int TotalErrors);
