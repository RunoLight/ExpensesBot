namespace ExpensesBot.Data.Entities;

public sealed class Category
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public User User { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public string? Icon { get; set; }

    public bool IsArchived { get; set; }
}