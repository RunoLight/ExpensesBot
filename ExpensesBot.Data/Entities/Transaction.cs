namespace ExpensesBot.Data.Entities;

public enum TransactionType
{
    Expense = 0,
    Income = 1
}

public sealed class Transaction
{
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

    public List<TransactionTag> TransactionTags { get; set; } = [];
}