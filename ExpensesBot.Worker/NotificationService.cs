using System.Globalization;
using Newtonsoft.Json;

namespace ExpensesBot.Worker;

/// <summary>
/// Sends scheduled messages to users. Messages are plain text on purpose:
/// with parse_mode=Markdown, a "_" or "*" in a user or category name makes
/// Telegram reject the whole message.
/// </summary>
public sealed class NotificationService(
    ILogger<NotificationService> logger,
    IHttpClientFactory httpClientFactory)
{
    // "12 345,67" without depending on ICU culture data being installed.
    private static readonly NumberFormatInfo MoneyFormat = new()
    {
        NumberGroupSeparator = "\u00A0",
        NumberDecimalSeparator = ","
    };

    public Task SendDailyReminderAsync(
        string botToken,
        long telegramUserId,
        string displayName,
        long todaySpentMinor,
        string currency,
        CancellationToken ct = default)
    {
        var text = $"🌆 Добрый вечер, {displayName}!\n\n" +
                   (todaySpentMinor > 0
                       ? $"Сегодня записано расходов: {FormatMoney(todaySpentMinor, currency)}.\nНичего не забыли?"
                       : "Сегодня расходов ещё не записано — отправьте сумму в чат, и я помогу.");

        return SendAsync(botToken, telegramUserId, text, "daily reminder", ct);
    }

    public Task SendWeeklyReportAsync(
        string botToken,
        long telegramUserId,
        string displayName,
        long weekSpentMinor,
        string currency,
        IReadOnlyList<(string Name, long AmountMinor)> topCategories,
        CancellationToken ct = default)
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

        return SendAsync(botToken, telegramUserId, string.Join("\n", lines), "weekly report", ct);
    }

    public Task SendBudgetAlertAsync(
        string botToken,
        long telegramUserId,
        string categoryName,
        long spentMinor,
        long budgetMinor,
        string currency,
        CancellationToken ct = default)
    {
        var percent = budgetMinor > 0 ? spentMinor * 100m / budgetMinor : 0m;

        var text = $"⚠️ Бюджет «{categoryName}» почти исчерпан!\n" +
                   $"Потрачено {FormatMoney(spentMinor, currency)} из {FormatMoney(budgetMinor, currency)} ({percent:F0}%).";

        return SendAsync(botToken, telegramUserId, text, "budget alert", ct);
    }

    public static string FormatMoney(long amountMinor, string currency) =>
        $"{(amountMinor / 100m).ToString("N2", MoneyFormat)} {currency}";

    private async Task SendAsync(
        string botToken,
        long telegramUserId,
        string text,
        string kind,
        CancellationToken ct)
    {
        try
        {
            var http = httpClientFactory.CreateClient();
            var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
            var payload = JsonConvert.SerializeObject(new { chat_id = telegramUserId, text });

            using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(url, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Telegram rejected {Kind} for user {UserId}: {Status} {Body}",
                    kind, telegramUserId, (int)response.StatusCode,
                    await response.Content.ReadAsStringAsync(ct));
                return;
            }

            logger.LogInformation("Sent {Kind} to user {UserId}", kind, telegramUserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to send {Kind} to user {UserId}", kind, telegramUserId);
        }
    }
}
