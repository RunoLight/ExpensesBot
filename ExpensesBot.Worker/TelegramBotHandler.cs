using System.Globalization;
using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ExpensesBot.Worker;

/// <summary>
/// Handles Telegram updates. The handler is a singleton shared by all chats,
/// so it keeps no per-user state: everything a button needs (transaction type
/// and amount) travels inside its callback_data.
/// </summary>
public sealed class TelegramBotHandler(
    ILogger<TelegramBotHandler> logger,
    IHttpClientFactory httpClientFactory,
    IServiceProvider serviceProvider,
    IConfiguration configuration)
{
    private const decimal MaxAmount = 1_000_000_000m;
    private const string RecordPrefix = "tx:";       // tx:e:500 / tx:i:500
    private const string LegacyConfirmPrefix = "confirm:"; // buttons sent by older versions
    private const string CancelData = "cancel";

    private string WebAppUrl =>
        configuration["Telegram:WebAppUrl"] ?? "https://runolight.github.io/ExpensesBot/";

    public async Task<bool> ProcessUpdateAsync(
        string botToken,
        JObject update,
        CancellationToken ct)
    {
        try
        {
            if (update["callback_query"] is JObject callbackQuery)
                return await HandleCallbackQueryAsync(botToken, callbackQuery, ct);

            if (update["message"] is JObject message)
                return await HandleMessageAsync(botToken, message, ct);

            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error processing update {UpdateId}", update["update_id"]);
            return false;
        }
    }

    private async Task<bool> HandleMessageAsync(
        string botToken,
        JObject message,
        CancellationToken ct)
    {
        var text = message["text"]?.Value<string>();
        var chatId = message["chat"]?["id"]?.Value<long>();
        var telegramUserId = message["from"]?["id"]?.Value<long>();

        if (string.IsNullOrWhiteSpace(text) || chatId is null)
            return false;

        var command = text.Trim().Split(' ', '@')[0].ToLowerInvariant();

        switch (command)
        {
            case "/start":
                await SendStartMessageAsync(botToken, chatId.Value, message["from"]?["first_name"]?.Value<string>(), ct);
                return true;

            case "/expenses":
                await SendWebAppMessageAsync(botToken, chatId.Value, "📊 Откройте приложение для учёта финансов:", "Открыть ExpensesBot", WebAppUrl, ct);
                return true;

            case "/income":
                await SendWebAppMessageAsync(botToken, chatId.Value, "📈 Откройте приложение, чтобы записать доход:", "📈 Записать доход", WebAppUrl, ct);
                return true;

            case "/report":
                if (telegramUserId.HasValue)
                    await SendWeeklyReportAsync(botToken, chatId.Value, telegramUserId.Value, ct);
                return true;

            case "/menu":
                await SendMenuMessageAsync(botToken, chatId.Value, ct);
                return true;
        }

        if (TryParseAmount(text, out var amount) && telegramUserId.HasValue)
        {
            await AskTransactionTypeAsync(botToken, chatId.Value, telegramUserId.Value, amount, ct);
            return true;
        }

        return false;
    }

    private async Task<bool> HandleCallbackQueryAsync(
        string botToken,
        JObject callbackQuery,
        CancellationToken ct)
    {
        var callbackId = callbackQuery["id"]?.Value<string>() ?? "";
        var data = callbackQuery["data"]?.Value<string>() ?? "";
        var chatId = callbackQuery["message"]?["chat"]?["id"]?.Value<long>();
        var messageId = callbackQuery["message"]?["message_id"]?.Value<long>();
        var sender = callbackQuery["from"];
        var telegramUserId = sender?["id"]?.Value<long>();

        try
        {
            if (chatId is null)
                return false;

            switch (data)
            {
                case CancelData:
                case "cancel_expense":
                    await EditOrSendAsync(botToken, chatId.Value, messageId, "Отменено.", ct);
                    return true;

                case "menu_stats":
                    await SendWeeklyReportAsync(botToken, chatId.Value, telegramUserId ?? 0, ct);
                    return true;

                case "menu_income":
                case "menu_expense":
                    await SendMessageAsync(botToken, chatId.Value,
                        "✏️ Отправьте сумму сообщением (например: 1500), а я спрошу, расход это или доход.", null, ct);
                    return true;

                case "menu_goals":
                case "menu_categories":
                    await SendWebAppMessageAsync(botToken, chatId.Value,
                        "Цели и категории настраиваются в приложении:", "Открыть ExpensesBot", WebAppUrl, ct);
                    return true;
            }

            if (telegramUserId is null || !TryParseRecordCallback(data, out var type, out var amount))
                return false;

            var user = await GetOrCreateUserAsync(telegramUserId.Value, sender?["first_name"]?.Value<string>(), ct);
            var recorded = await RecordTransactionAsync(user, amount, type, ct);

            var label = type == TransactionType.Income ? "Доход" : "Расход";
            var result = recorded
                ? $"✅ {label} {FormatAmount(amount)} {user.DefaultCurrency} записан.\n\nПодробная статистика — в приложении: /expenses"
                : "Не удалось записать. Откройте приложение /expenses и попробуйте снова.";

            // Replacing the message text also removes the buttons, so the
            // same transaction can't be recorded twice by tapping again.
            await EditOrSendAsync(botToken, chatId.Value, messageId, result, ct);
            return true;
        }
        finally
        {
            await AnswerCallbackQueryAsync(botToken, callbackId, ct);
        }
    }

    public static bool TryParseAmount(string text, out decimal amount)
    {
        var normalized = text.Trim().Replace(" ", "").Replace(',', '.');

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) &&
               amount > 0 &&
               amount <= MaxAmount &&
               decimal.Round(amount, 2) == amount;
    }

    public static string BuildRecordCallback(TransactionType type, decimal amount) =>
        $"{RecordPrefix}{(type == TransactionType.Income ? "i" : "e")}:{amount.ToString("0.##", CultureInfo.InvariantCulture)}";

    public static bool TryParseRecordCallback(string data, out TransactionType type, out decimal amount)
    {
        type = TransactionType.Expense;
        amount = 0;
        string amountText;

        if (data.StartsWith(RecordPrefix, StringComparison.Ordinal))
        {
            var parts = data[RecordPrefix.Length..].Split(':');
            if (parts.Length != 2 || parts[0] is not ("e" or "i"))
                return false;

            type = parts[0] == "i" ? TransactionType.Income : TransactionType.Expense;
            amountText = parts[1];
        }
        else if (data.StartsWith(LegacyConfirmPrefix, StringComparison.Ordinal))
        {
            amountText = data[LegacyConfirmPrefix.Length..];
        }
        else
        {
            return false;
        }

        return TryParseAmount(amountText, out amount);
    }

    private async Task AskTransactionTypeAsync(
        string botToken,
        long chatId,
        long telegramUserId,
        decimal amount,
        CancellationToken ct)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        var currency = await db.Users
            .Where(x => x.TelegramUserId == telegramUserId)
            .Select(x => x.DefaultCurrency)
            .FirstOrDefaultAsync(ct) ?? Currencies.Default;

        var keyboard = new
        {
            inline_keyboard = new object[][]
            {
                [
                    new { text = "➖ Расход", callback_data = BuildRecordCallback(TransactionType.Expense, amount) },
                    new { text = "➕ Доход", callback_data = BuildRecordCallback(TransactionType.Income, amount) }
                ],
                [
                    new { text = "❌ Отмена", callback_data = CancelData }
                ]
            }
        };

        await SendMessageAsync(botToken, chatId, $"💰 {FormatAmount(amount)} {currency} — это расход или доход?", keyboard, ct);
    }

    private async Task<User> GetOrCreateUserAsync(long telegramUserId, string? firstName, CancellationToken ct)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, ct);

        if (user is not null)
            return user;

        user = UserFactory.Create(telegramUserId, firstName);
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
            return user;
        }
        catch (DbUpdateException)
        {
            // Created concurrently by the API (first Mini App launch).
            db.ChangeTracker.Clear();
            return await db.Users.AsNoTracking().FirstAsync(x => x.TelegramUserId == telegramUserId, ct);
        }
    }

    private async Task<bool> RecordTransactionAsync(User user, decimal amount, TransactionType type, CancellationToken ct)
    {
        var amountMinor = decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
        if (amountMinor <= 0)
            return false;

        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        db.Transactions.Add(new Transaction
        {
            UserId = user.Id,
            AmountMinor = amountMinor,
            Currency = user.DefaultCurrency,
            Type = type,
            Note = type == TransactionType.Income ? "Доход из Telegram" : "Из Telegram",
            OccurredAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task SendWeeklyReportAsync(string botToken, long chatId, long telegramUserId, CancellationToken ct)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, ct);

        if (user is null)
        {
            await SendMessageAsync(botToken, chatId, "Пока нет данных. Отправьте сумму расхода, например: 1500", null, ct);
            return;
        }

        var weekAgo = DateTime.UtcNow.AddDays(-7);

        var totals = await db.Transactions
            .Where(t => t.UserId == user.Id &&
                        t.Currency == user.DefaultCurrency &&
                        t.OccurredAtUtc >= weekAgo)
            .GroupBy(t => t.Type)
            .Select(g => new { Type = g.Key, Total = g.Sum(t => t.AmountMinor) })
            .ToListAsync(ct);

        var topCategories = await db.Transactions
            .Where(t => t.UserId == user.Id &&
                        t.Type == TransactionType.Expense &&
                        t.Currency == user.DefaultCurrency &&
                        t.OccurredAtUtc >= weekAgo)
            .GroupBy(t => t.Category != null ? t.Category.Name : "Без категории")
            .Select(g => new { Name = g.Key, Total = g.Sum(t => t.AmountMinor) })
            .OrderByDescending(x => x.Total)
            .Take(5)
            .ToListAsync(ct);

        var expense = totals.FirstOrDefault(x => x.Type == TransactionType.Expense)?.Total ?? 0;
        var income = totals.FirstOrDefault(x => x.Type == TransactionType.Income)?.Total ?? 0;
        var currency = user.DefaultCurrency;

        var lines = new List<string>
        {
            "📊 Последние 7 дней",
            "",
            $"➖ Расходы: {NotificationService.FormatMoney(expense, currency)}",
            $"➕ Доходы: {NotificationService.FormatMoney(income, currency)}"
        };

        if (topCategories.Count > 0)
        {
            lines.Add("");
            lines.Add("📂 Топ категорий:");
            lines.AddRange(topCategories.Select(c => $"  • {c.Name}: {NotificationService.FormatMoney(c.Total, currency)}"));
        }

        lines.Add("");
        lines.Add($"Учитываются операции в {currency}. Полная статистика — /expenses");

        await SendMessageAsync(botToken, chatId, string.Join("\n", lines), null, ct);
    }

    private Task SendStartMessageAsync(string botToken, long chatId, string? firstName, CancellationToken ct)
    {
        var text = $"Привет, {firstName ?? "друг"}! 👋\n\n" +
                   "Я — трекер личных финансов. Вот что я умею:\n\n" +
                   "/menu — главное меню\n" +
                   "/expenses — открыть приложение\n" +
                   "/report — сводка за 7 дней\n\n" +
                   "Или просто отправьте сумму (например: 1500) — я спрошу, расход это или доход, и запишу.";

        return SendMessageAsync(botToken, chatId, text, null, ct);
    }

    private Task SendWebAppMessageAsync(
        string botToken, long chatId, string text, string buttonText, string url, CancellationToken ct)
    {
        var keyboard = new
        {
            inline_keyboard = new[]
            {
                new[] { new { text = buttonText, web_app = new { url } } }
            }
        };

        return SendMessageAsync(botToken, chatId, text, keyboard, ct);
    }

    private Task SendMenuMessageAsync(string botToken, long chatId, CancellationToken ct)
    {
        var keyboard = new
        {
            inline_keyboard = new object[][]
            {
                [
                    new { text = "📊 Сводка", callback_data = "menu_stats" },
                    new { text = "✏️ Записать", callback_data = "menu_expense" }
                ],
                [
                    new { text = "🎯 Цели", callback_data = "menu_goals" },
                    new { text = "📁 Категории", callback_data = "menu_categories" }
                ],
                [
                    new { text = "📱 Открыть приложение", web_app = new { url = WebAppUrl } }
                ]
            }
        };

        return SendMessageAsync(botToken, chatId,
            "📱 Главное меню:\n\nВыберите действие или откройте приложение.", keyboard, ct);
    }

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.##", CultureInfo.InvariantCulture);

    private Task SendMessageAsync(string botToken, long chatId, string text, object? replyMarkup, CancellationToken ct) =>
        CallTelegramAsync(botToken, "sendMessage", new { chat_id = chatId, text, reply_markup = replyMarkup }, ct);

    private Task EditOrSendAsync(string botToken, long chatId, long? messageId, string text, CancellationToken ct) =>
        messageId is null
            ? SendMessageAsync(botToken, chatId, text, null, ct)
            : CallTelegramAsync(botToken, "editMessageText", new { chat_id = chatId, message_id = messageId.Value, text }, ct);

    private Task AnswerCallbackQueryAsync(string botToken, string callbackId, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(callbackId)
            ? Task.CompletedTask
            : CallTelegramAsync(botToken, "answerCallbackQuery", new { callback_query_id = callbackId }, ct);

    private async Task CallTelegramAsync(string botToken, string method, object payload, CancellationToken ct)
    {
        try
        {
            var http = httpClientFactory.CreateClient();
            var json = JsonConvert.SerializeObject(
                payload,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var response = await http.PostAsync($"https://api.telegram.org/bot{botToken}/{method}", content, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Telegram {Method} failed: {Status} {Body}",
                    method, (int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Telegram {Method} call failed", method);
        }
    }
}
