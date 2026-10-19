using System.Globalization;
using ExpensesBot.Data.Entities;

namespace ExpensesBot.Data;

/// <summary>
/// Texts the API and the Worker both write for a user: notifications, automatic
/// notes, default categories, CSV headers. Each one holds both translations next
/// to each other (<see cref="Pick"/>), so a missing translation doesn't compile.
/// Messages are plain text: with parse_mode=Markdown, a "_" or "*" in a user or
/// category name would make Telegram reject the whole message.
/// </summary>
public sealed class MessageText
{
    public static readonly MessageText En = new(Languages.English);
    public static readonly MessageText Ru = new(Languages.Russian);

    public static MessageText For(string? language) => language == Languages.Russian ? Ru : En;

    // "12 345,67" without depending on ICU culture data being installed.
    private static readonly NumberFormatInfo RussianMoney = new()
    {
        NumberGroupSeparator = " ",
        NumberDecimalSeparator = ","
    };

    private MessageText(string language) => Language = language;

    public string Language { get; }

    public bool IsRussian => Language == Languages.Russian;

    private string Pick(string en, string ru) => IsRussian ? ru : en;

    /// <summary>"1,500.00 AMD" / "1 500,00 AMD".</summary>
    public string Money(long amountMinor, string currency) =>
        $"{(amountMinor / 100m).ToString("N2", IsRussian ? RussianMoney : NumberFormatInfo.InvariantInfo)} {currency}";

    public string NoCategory => Pick("No category", "Без категории");

    public string OverallBudget => Pick("Overall", "Общий");

    /// <summary>How to address someone without a display name.</summary>
    public string FallbackName => Pick("there", "друг");

    public string MemberFallbackName(long userId) => Pick($"Member {userId}", $"Участник {userId}");

    public string GoalContributionNote(string goalName) => Pick($"Contribution to goal: {goalName}", $"Вклад в цель: {goalName}");

    /// <summary>Note of a transaction created by a recurring rule.</summary>
    public string AutoNote(string? note, TransactionType type) =>
        note is not null
            ? note + Pick(" (auto)", " (авто)")
            : type == TransactionType.Income ? Pick("Auto income", "Авто-доход") : Pick("Auto expense", "Авто-расход");

    public string TransactionTypeName(TransactionType type) =>
        type == TransactionType.Income ? Pick("Income", "Доход") : Pick("Expense", "Расход");

    /// <summary>Header of the CSV export; the Mini App's import reads either language.</summary>
    public string CsvHeader => Pick("Date (UTC),Type,Category,Amount,Currency,Note", "Дата (UTC),Тип,Категория,Сумма,Валюта,Комментарий");

    /// <summary>Categories every new account starts with.</summary>
    public IReadOnlyList<(string Name, string Icon)> DefaultCategories => IsRussian
        ?
        [
            ("Продукты", "🛒"), ("Транспорт", "🚕"), ("Дом", "🏠"),
            ("Кафе", "☕"), ("Зарплата", "💼"), ("Другое", "📦")
        ]
        :
        [
            ("Groceries", "🛒"), ("Transport", "🚕"), ("Home", "🏠"),
            ("Cafe", "☕"), ("Salary", "💼"), ("Other", "📦")
        ];

    // Notifications

    public string DailyReminder(string displayName, long todaySpentMinor, string currency) =>
        Pick($"🌆 Good evening, {displayName}!\n\n", $"🌆 Добрый вечер, {displayName}!\n\n") +
        (todaySpentMinor > 0
            ? Pick($"Expenses recorded today: {Money(todaySpentMinor, currency)}.\nForgot anything?",
                $"Сегодня записано расходов: {Money(todaySpentMinor, currency)}.\nНичего не забыли?")
            : Pick("No expenses recorded today yet — send an amount to the chat and I'll help.",
                "Сегодня расходов ещё не записано — отправьте сумму в чат, и я помогу."));

    public string WeeklyReport(
        string displayName,
        long weekSpentMinor,
        string currency,
        IReadOnlyList<(string Name, long AmountMinor)> topCategories)
    {
        var lines = new List<string>
        {
            Pick($"📊 Weekly report, {displayName}!", $"📊 Еженедельный отчёт, {displayName}!"),
            "",
            Pick($"💰 Spent this week: {Money(weekSpentMinor, currency)}", $"💰 Расходы за неделю: {Money(weekSpentMinor, currency)}")
        };

        if (topCategories.Count > 0)
        {
            lines.Add("");
            lines.Add(Pick("📂 Top categories:", "📂 Топ категорий:"));
            lines.AddRange(topCategories.Select(c => $"  • {c.Name}: {Money(c.AmountMinor, currency)}"));
        }

        return string.Join("\n", lines);
    }

    public string BudgetAlert(string categoryName, long spentMinor, long budgetMinor, string currency)
    {
        var percent = (budgetMinor > 0 ? spentMinor * 100m / budgetMinor : 0m).ToString("F0", CultureInfo.InvariantCulture);
        var spent = Money(spentMinor, currency);
        var limit = Money(budgetMinor, currency);

        return Pick(
            $"⚠️ The “{categoryName}” budget is almost used up!\nSpent {spent} of {limit} ({percent}%).",
            $"⚠️ Бюджет «{categoryName}» почти исчерпан!\nПотрачено {spent} из {limit} ({percent}%).");
    }

    public string WalletTransaction(string author, string walletName, TransactionType type, long amountMinor, string currency,
        string? categoryName, string? note)
    {
        var sign = type == TransactionType.Income ? "➕" : "➖";
        var details = string.Join(" · ", new[] { categoryName, note }.Where(x => !string.IsNullOrWhiteSpace(x)));

        return Pick($"👥 {author} — wallet “{walletName}”\n", $"👥 {author} — кошелёк «{walletName}»\n") +
               $"{sign} {Money(amountMinor, currency)}" +
               (details.Length > 0 ? $" — {details}" : "");
    }
}
