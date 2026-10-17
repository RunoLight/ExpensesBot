namespace ExpensesBot.Data.Entities;

public sealed class Budget
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public User User { get; set; } = null!;

    public long? CategoryId { get; set; }

    public Category? Category { get; set; }

    public long AmountMinor { get; set; }

    public string Currency { get; set; } = "AMD";

    public DateTime StartDateUtc { get; set; } = DateTime.UtcNow;

    public DateTime EndDateUtc { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
