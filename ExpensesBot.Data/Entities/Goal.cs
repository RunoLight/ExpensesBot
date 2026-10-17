namespace ExpensesBot.Data.Entities;

public sealed class Goal
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public User User { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public long TargetAmountMinor { get; set; }

    public string Currency { get; set; } = "AMD";

    public long? CategoryId { get; set; }

    public Category? Category { get; set; }

    public DateTime? TargetDateUtc { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
