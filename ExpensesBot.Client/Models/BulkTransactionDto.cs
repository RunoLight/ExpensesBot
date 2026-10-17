namespace ExpensesBot.Client.Models;

public sealed record BulkTransactionRequest(
    long AmountMinor,
    string Currency,
    long? CategoryId,
    DateTime OccurredAtUtc,
    string? Note,
    TransactionType Type);

public sealed record BulkCreateResponse(
    List<TransactionDto> Created,
    List<BulkErrorDto> Errors);

public sealed record BulkErrorDto(int Index, string Error);
