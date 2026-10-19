using ExpensesBot.Data;

namespace ExpensesBot.Worker;

/// <summary>
/// Texts of scheduled messages. They are plain text on purpose: with
/// parse_mode=Markdown, a "_" or "*" in a user or category name makes Telegram
/// reject the whole message. Delivery goes through the outbox
/// (<see cref="OutboxDispatcher"/>).
/// </summary>
public static class NotificationTexts
{
    public const string DailyReminderKind = "daily-reminder";
    public const string WeeklyReportKind = "weekly-report";
    public const string BudgetAlertKind = "budget-alert";

    public static string DailyReminder(string displayName, long todaySpentMinor, string currency) =>
        $"🌆 Добрый вечер, {displayName}!\n\n" +
        (todaySpentMinor > 0
            ? $"Сегодня записано расходов: {FormatMoney(todaySpentMinor, currency)}.\nНичего не забыли?"
            : "Сегодня расходов ещё не записано — отправьте сумму в чат, и я помогу.");

    public static string WeeklyReport(
        string displayName,
        long weekSpentMinor,
        string currency,
        IReadOnlyList<(string Name, long AmountMinor)> topCategories)
    {
        var lines = new List<string>
        {
            $"📊 Еженедельный отчёт, {displayName}!",
            "",
            $"💰 Расходы за неделю: {FormatMoney(weekSpentMinor, currency)}"
        };

        if (topCategories.Count > 0)
        {
            lines.Add("");
            lines.Add("📂 Топ категорий:");
            lines.AddRange(topCategories.Select(c => $"  • {c.Name}: {FormatMoney(c.AmountMinor, currency)}"));
        }

        return string.Join("\n", lines);
    }

    public static string BudgetAlert(string categoryName, long spentMinor, long budgetMinor, string currency)
    {
        var percent = budgetMinor > 0 ? spentMinor * 100m / budgetMinor : 0m;

        return $"⚠️ Бюджет «{categoryName}» почти исчерпан!\n" +
               $"Потрачено {FormatMoney(spentMinor, currency)} из {FormatMoney(budgetMinor, currency)} ({percent:F0}%).";
    }

    public static string FormatMoney(long amountMinor, string currency) =>
        Currencies.FormatMoney(amountMinor, currency);
}
