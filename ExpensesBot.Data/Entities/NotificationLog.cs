using System.Globalization;

namespace ExpensesBot.Data.Entities;

public enum NotificationKind
{
    DailyReminder = 1,
    WeeklyReport = 2,
    BudgetAlert = 3
}

/// <summary>
/// A scheduled notification that has already been sent. The Worker checks this
/// table before sending, so a restart (or a second Worker instance) never sends
/// the same reminder, report or budget alert twice.
/// </summary>
public sealed class NotificationLog
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public User User { get; set; } = null!;

    public NotificationKind Kind { get; set; }

    /// <summary>
    /// What the notification was about: the UTC date (yyyy-MM-dd) for reminders
    /// and weekly reports, the budget id for budget alerts.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>When the notification was queued in the outbox.</summary>
    public DateTime SentAtUtc { get; set; }

    public static string DateKey(DateTime dateUtc) =>
        dateUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string BudgetKey(long budgetId) =>
        budgetId.ToString(CultureInfo.InvariantCulture);
}
