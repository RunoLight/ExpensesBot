namespace ExpensesBot.Data.Entities;

public enum RecurrencePattern
{
    None = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
    Yearly = 4
}

public sealed class RecurringTransaction
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public User User { get; set; } = null!;

    public long? CategoryId { get; set; }

    public Category? Category { get; set; }

    public TransactionType Type { get; set; } = TransactionType.Expense;

    public long AmountMinor { get; set; }

    public string Currency { get; set; } = "AMD";

    public string? Note { get; set; }

    public RecurrencePattern Pattern { get; set; } = RecurrencePattern.Monthly;

    public DateTime? EndsAfter { get; set; }

    public int TimesExecuted { get; set; }

    public DateTime LastExecutedAtUtc { get; set; } = DateTime.MinValue;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
