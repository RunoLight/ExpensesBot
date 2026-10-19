namespace ExpensesBot.Data.Entities;

public enum TransactionType
{
    Expense = 0,
    Income = 1
}

public sealed class Transaction
{
    public const int MaxIdempotencyKeyLength = 64;

    public long Id { get; set; }

    public long UserId { get; set; }

    public User User { get; set; } = null!;

    public long? CategoryId { get; set; }

    public Category? Category { get; set; }

    public TransactionType Type { get; set; } = TransactionType.Expense;

    // Сумма в минимальных единицах валюты
    public long AmountMinor { get; set; }

    public string Currency { get; set; } = "AMD";

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string? Note { get; set; }

    /// <summary>Shared wallet the transaction was recorded into, if any.</summary>
    public long? WalletId { get; set; }

    public Wallet? Wallet { get; set; }

    /// <summary>
    /// Client-supplied Idempotency-Key the transaction was created with. Unique per
    /// user, so a retried or double-submitted request finds this row instead of
    /// creating a second one.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    public List<TransactionTag> TransactionTags { get; set; } = [];
}