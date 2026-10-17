namespace ExpensesBot.Data.Entities;

public sealed class Tag
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public User User { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = "#36A2EB";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<TransactionTag> TransactionTags { get; set; } = [];
}

public sealed class TransactionTag
{
    public long TransactionId { get; set; }

    public Transaction Transaction { get; set; } = null!;

    public long TagId { get; set; }

    public Tag Tag { get; set; } = null!;
}
