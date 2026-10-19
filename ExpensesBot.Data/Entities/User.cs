namespace ExpensesBot.Data.Entities;

public sealed class User
{
    public long Id { get; set; }

    /// <summary>Telegram user id (also the private chat id).</summary>
    public long TelegramUserId { get; set; }

    public string? DisplayName { get; set; }

    public string DefaultCurrency { get; set; } = "AMD";

    /// <summary>"en" or "ru": the language of the Mini App, the bot and notifications.</summary>
    public string Language { get; set; } = Languages.Default;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<Category> Categories { get; set; } = [];

    public List<Transaction> Transactions { get; set; } = [];

    public List<RecurringTransaction> RecurringTransactions { get; set; } = [];

    public List<Budget> Budgets { get; set; } = [];
}