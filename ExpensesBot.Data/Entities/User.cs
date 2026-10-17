namespace ExpensesBot.Data.Entities;

public sealed class User
{
    public long Id { get; set; }

    // Telegram user ID
    public long TelegramUserId { get; set; }

    public string? DisplayName { get; set; }

    public string DefaultCurrency { get; set; } = "AMD";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<Category> Categories { get; set; } = [];

    public List<Transaction> Transactions { get; set; } = [];

    public List<RecurringTransaction> RecurringTransactions { get; set; } = [];

    public List<Budget> Budgets { get; set; } = [];
}