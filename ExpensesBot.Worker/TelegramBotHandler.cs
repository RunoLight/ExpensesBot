using System.Globalization;
using ExpensesBot.Data;
using ExpensesBot.Data.Entities;
using ExpensesBot.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ExpensesBot.Worker;

/// <summary>
/// Handles Telegram updates. The handler is a singleton shared by all chats,
/// so it keeps no per-user state: everything a button needs (type, amount,
/// currency, category) travels inside its callback_data, and the note is read
/// back from the user's message that the bot's question replies to.
/// </summary>
public sealed class TelegramBotHandler(
    ILogger<TelegramBotHandler> logger,
    IHttpClientFactory httpClientFactory,
    IServiceProvider serviceProvider,
    IConfiguration configuration)
{
    private const decimal MaxAmount = BotEntryParser.MaxAmount;
    private const string LegacyConfirmPrefix = "confirm:"; // buttons sent by older versions
    private const string CancelData = "cancel";
    private const string KeepData = "keep";

    public const string EntryHint =
        "✏️ Отправьте сумму и, если хотите, описание:\n" +
        "• 1500 кофе\n" +
        "• +50000 зарплата — плюс значит доход\n" +
        "• 20$ такси или такси 700 руб — другая валюта\n" +
        "• 1,5к продукты — тысячи";

    private string WebAppUrl =>
        configuration["Telegram:WebAppUrl"] ?? "https://runolight.github.io/ExpensesBot/";

    public async Task<bool> ProcessUpdateAsync(
        string botToken,
        JObject update,
        CancellationToken ct)
    {
        var type = update["callback_query"] is not null ? "callback" : update["message"] is not null ? "message" : "other";

        // The Worker has no incoming HTTP requests, so each update is a trace root.
        using var activity = Telemetry.ActivitySource.StartActivity("bot.update");
        activity?.SetTag("telegram.update.type", type);
        Telemetry.BotUpdates.Add(1, new KeyValuePair<string, object?>("type", type));

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
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
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
        var messageId = message["message_id"]?.Value<long>();
        var telegramUserId = message["from"]?["id"]?.Value<long>();
        var firstName = message["from"]?["first_name"]?.Value<string>();

        if (string.IsNullOrWhiteSpace(text) || chatId is null)
            return false;

        var command = text.Trim().Split(' ', '@')[0].ToLowerInvariant();

        switch (command)
        {
            case "/start":
                // t.me/<bot>?start=join_CODE arrives as "/start join_CODE".
                if (TryReadJoinCode(text, out var inviteCode) && telegramUserId.HasValue)
                    await JoinWalletAsync(botToken, chatId.Value, telegramUserId.Value, firstName, inviteCode, ct);
                else
                    await SendStartMessageAsync(botToken, chatId.Value, firstName, ct);
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

            case "/undo":
                if (telegramUserId.HasValue)
                    await OfferUndoAsync(botToken, chatId.Value, telegramUserId.Value, ct);
                return true;
        }

        if (telegramUserId.HasValue && TryParseEntry(text, out var entry))
        {
            await AskToConfirmAsync(botToken, chatId.Value, messageId, telegramUserId.Value, firstName, entry, ct);
            return true;
        }

        // Looks like an attempt to record something: explain the format.
        if (text.Any(char.IsDigit))
        {
            await SendMessageAsync(botToken, chatId.Value, "Не понял сумму. " + EntryHint, null, ct);
            return true;
        }

        return false;
    }

    /// <summary>The invite code from "/start join_ABCD2345".</summary>
    public static bool TryReadJoinCode(string text, out string code)
    {
        var parts = text.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        code = parts.Length == 2 && parts[1].StartsWith(WalletInvites.DeepLinkPrefix, StringComparison.Ordinal)
            ? parts[1][WalletInvites.DeepLinkPrefix.Length..]
            : string.Empty;

        return WalletInvites.Normalize(code) is not null;
    }

    private async Task JoinWalletAsync(
        string botToken, long chatId, long telegramUserId, string? firstName, string code, CancellationToken ct)
    {
        var user = await GetOrCreateUserAsync(telegramUserId, firstName, ct);

        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        var result = await WalletInvites.JoinAsync(db, user.Id, code, DateTime.UtcNow, ct);

        var text = result.Status switch
        {
            JoinWalletStatus.Joined =>
                $"👥 Вы присоединились к общему кошельку «{result.Wallet!.Name}».\n\n" +
                "Операции, записанные в него, видят все участники. Выбрать кошелёк при записи и посмотреть общую сводку можно в приложении.",
            JoinWalletStatus.AlreadyMember =>
                $"Вы уже участник кошелька «{result.Wallet!.Name}».",
            JoinWalletStatus.Full =>
                $"В кошельке «{result.Wallet!.Name}» уже {WalletInvites.MaxMembers} участников — больше добавить нельзя.",
            _ => "Приглашение не найдено: возможно, владелец сменил код. Попросите новую ссылку."
        };

        await SendWebAppMessageAsync(botToken, chatId, text, "👥 Открыть кошельки", WebAppUrl, ct);
    }

    /// <summary>A bare amount ("1 500,50") or a free-form entry ("1500 кофе", "+20$ возврат").</summary>
    public static bool TryParseEntry(string text, out BotEntry entry)
    {
        if (TryParseAmount(text, out var amount))
        {
            entry = new BotEntry(amount, null, null, null);
            return true;
        }

        return BotEntryParser.TryParse(text, out entry);
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

                case KeepData:
                    await EditOrSendAsync(botToken, chatId.Value, messageId, "Хорошо, запись осталась.", ct);
                    return true;

                case "menu_stats":
                    await SendWeeklyReportAsync(botToken, chatId.Value, telegramUserId ?? 0, ct);
                    return true;

                case "menu_undo":
                    if (telegramUserId.HasValue)
                        await OfferUndoAsync(botToken, chatId.Value, telegramUserId.Value, ct);
                    return true;

                case "menu_income":
                case "menu_expense":
                    await SendMessageAsync(botToken, chatId.Value, EntryHint, null, ct);
                    return true;

                case "menu_goals":
                case "menu_categories":
                    await SendWebAppMessageAsync(botToken, chatId.Value,
                        "Цели и категории настраиваются в приложении:", "Открыть ExpensesBot", WebAppUrl, ct);
                    return true;
            }

            if (telegramUserId is not null && BotUndo.TryParseCallback(data, out var undoId))
            {
                await UndoAsync(botToken, chatId.Value, messageId, telegramUserId.Value, undoId, ct);
                return true;
            }

            if (telegramUserId is not null && RecordCallback.TryParseTypeStep(data, out var typeStep))
            {
                var stepUser = await GetOrCreateUserAsync(telegramUserId.Value, sender?["first_name"]?.Value<string>(), ct);
                await ShowCategoryPickerAsync(botToken, chatId.Value, stepUser, typeStep,
                    ReadNoteFromQuestion(callbackQuery), messageId, null, ct);
                return true;
            }

            if (telegramUserId is null || !TryParseRecord(data, out var request))
                return false;

            var user = await GetOrCreateUserAsync(telegramUserId.Value, sender?["first_name"]?.Value<string>(), ct);
            var note = ReadNoteFromQuestion(callbackQuery);
            var recorded = await RecordTransactionAsync(user, request, note, ct);

            var label = request.Type == TransactionType.Income ? "Доход" : "Расход";

            if (recorded is not { } entry)
            {
                await EditOrSendAsync(botToken, chatId.Value, messageId,
                    "Не удалось записать. Откройте приложение /expenses и попробуйте снова.", ct);
                return true;
            }

            var result = $"✅ {label} {FormatAmount(request.Amount)} {entry.Transaction.Currency} записан" +
                         (entry.Category is { } category ? $" · {category.Icon} {category.Name}" : "") +
                         (note is not null ? $" · {note}" : "") +
                         ".\n\nПодробная статистика — в приложении: /expenses";

            // The record buttons are replaced with a single "undo", so the same
            // transaction can't be recorded twice by tapping again.
            var undoKeyboard = new
            {
                inline_keyboard = new[]
                {
                    new[] { new { text = "↩️ Отменить", callback_data = BotUndo.BuildCallback(entry.Transaction.Id) } }
                }
            };

            if (messageId is { } recordedMessageId)
                await EditMessageAsync(botToken, chatId.Value, recordedMessageId, result, undoKeyboard, ct);
            else
                await SendMessageAsync(botToken, chatId.Value, result, undoKeyboard, ct);

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
        new RecordCallback(type, amount).Build();

    public static bool TryParseRecordCallback(string data, out TransactionType type, out decimal amount)
    {
        var parsed = TryParseRecord(data, out var request);
        type = parsed ? request.Type : TransactionType.Expense;
        amount = parsed ? request.Amount : 0;
        return parsed;
    }

    /// <summary>Current <see cref="RecordCallback"/> buttons plus "confirm:500" from older versions (expenses).</summary>
    public static bool TryParseRecord(string data, out RecordCallback request)
    {
        if (RecordCallback.TryParse(data, out request))
            return true;

        if (data.StartsWith(LegacyConfirmPrefix, StringComparison.Ordinal) &&
            TryParseAmount(data[LegacyConfirmPrefix.Length..], out var amount))
        {
            request = new RecordCallback(TransactionType.Expense, amount);
            return true;
        }

        return false;
    }

    /// <summary>The note from the user's message the bot's question was a reply to.</summary>
    private static string? ReadNoteFromQuestion(JObject callbackQuery)
    {
        var original = callbackQuery["message"]?["reply_to_message"]?["text"]?.Value<string>();
        return original is not null && TryParseEntry(original, out var entry) ? entry.Note : null;
    }

    /// <summary>
    /// First answer to an entry: asks "expense or income?" unless the sign
    /// already said it, in which case it goes straight to the category picker.
    /// </summary>
    private async Task AskToConfirmAsync(
        string botToken,
        long chatId,
        long? replyToMessageId,
        long telegramUserId,
        string? firstName,
        BotEntry entry,
        CancellationToken ct)
    {
        var user = await GetOrCreateUserAsync(telegramUserId, firstName, ct);
        var currency = entry.Currency ?? user.DefaultCurrency;
        var guess = await GuessCategoryAsync(user.Id, entry.Note, ct);

        if (entry.Type is { } knownType)
        {
            var request = new RecordCallback(knownType, entry.Amount, currency, guess?.Id);
            await ShowCategoryPickerAsync(botToken, chatId, user, request, entry.Note, null, replyToMessageId, ct);
            return;
        }

        string TypeStep(TransactionType type) =>
            new RecordCallback(type, entry.Amount, currency, guess?.Id).BuildTypeStep();

        var keyboard = new
        {
            inline_keyboard = new object[][]
            {
                [
                    new { text = "➖ Расход", callback_data = TypeStep(TransactionType.Expense) },
                    new { text = "➕ Доход", callback_data = TypeStep(TransactionType.Income) }
                ],
                [
                    new { text = "❌ Отмена", callback_data = CancelData }
                ]
            }
        };

        var text = DescribeEntry(entry.Amount, currency, entry.Note, guess) + "\n\nЭто расход или доход?";

        // Replying to the user's message lets the button handlers read the note back.
        await SendMessageAsync(botToken, chatId, text, keyboard, ct, replyToMessageId);
    }

    /// <summary>
    /// Offers the guessed category and the user's most used ones as buttons.
    /// Edits the "expense or income?" message when there is one, so the chat
    /// keeps a single message per entry.
    /// </summary>
    private async Task ShowCategoryPickerAsync(
        string botToken,
        long chatId,
        User user,
        RecordCallback request,
        string? note,
        long? messageIdToEdit,
        long? replyToMessageId,
        CancellationToken ct)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        var categories = await LoadCategoriesAsync(db, user.Id, ct);
        var since = DateTime.UtcNow.AddDays(-90);

        var usage = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == user.Id &&
                        t.Type == request.Type &&
                        t.CategoryId != null &&
                        t.CreatedAtUtc >= since)
            .GroupBy(t => t.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, ct);

        var guess = categories.FirstOrDefault(c => c.Id == request.CategoryId);
        var options = CategoryGuesser.Suggest(categories, usage, guess?.Id);

        string Record(long? categoryId) => (request with { CategoryId = categoryId }).Build();

        var rows = options
            .Select(c => new
            {
                text = (c.Id == guess?.Id ? "⭐ " : "") + $"{c.Icon ?? "📁"} {c.Name}",
                callback_data = Record(c.Id)
            })
            .Chunk(2)
            .Select(row => row.Cast<object>().ToArray())
            .Append(new object[]
            {
                new { text = "🚫 Без категории", callback_data = Record(null) },
                new { text = "❌ Отмена", callback_data = CancelData }
            })
            .ToArray();

        var label = request.Type == TransactionType.Income ? "➕ Доход" : "➖ Расход";
        var text = DescribeEntry(request.Amount, request.Currency ?? user.DefaultCurrency, note, guess) +
                   $"\n\n{label} — выберите категорию:";
        var keyboard = new { inline_keyboard = rows };

        if (messageIdToEdit is { } messageId)
            await EditMessageAsync(botToken, chatId, messageId, text, keyboard, ct);
        else
            await SendMessageAsync(botToken, chatId, text, keyboard, ct, replyToMessageId);
    }

    public static string DescribeEntry(decimal amount, string currency, string? note, CategoryOption? category) =>
        $"💰 {FormatAmount(amount)} {currency}" +
        (note is not null ? $" · {note}" : "") +
        (category is not null ? $"\n📂 {category.Icon ?? "📁"} {category.Name}" : "");

    private async Task<CategoryOption?> GuessCategoryAsync(long userId, string? note, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(note))
            return null;

        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        var categories = await LoadCategoriesAsync(db, userId, ct);

        var history = await db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.Note != null && t.CategoryId != null)
            .OrderByDescending(t => t.Id)
            .Take(200)
            .Select(t => new { t.Note, t.CategoryId })
            .ToListAsync(ct);

        return CategoryGuesser.Guess(note, categories, history.Select(h => (h.Note!, h.CategoryId!.Value)));
    }

    private static Task<List<CategoryOption>> LoadCategoriesAsync(ExpensesDbContext db, long userId, CancellationToken ct) =>
        db.Categories
            .AsNoTracking()
            .Where(c => c.UserId == userId && !c.IsArchived)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryOption(c.Id, c.Name, c.Icon))
            .ToListAsync(ct);

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

    private sealed record RecordedEntry(Transaction Transaction, CategoryOption? Category);

    private async Task<RecordedEntry?> RecordTransactionAsync(User user, RecordCallback request, string? note, CancellationToken ct)
    {
        var amountMinor = decimal.ToInt64(decimal.Round(request.Amount * 100m, 0, MidpointRounding.AwayFromZero));
        if (amountMinor <= 0)
            return null;

        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        // The id comes from a button, so check that the category is still the user's and active.
        var category = request.CategoryId is { } categoryId
            ? await db.Categories
                .AsNoTracking()
                .Where(c => c.Id == categoryId && c.UserId == user.Id && !c.IsArchived)
                .Select(c => new CategoryOption(c.Id, c.Name, c.Icon))
                .FirstOrDefaultAsync(ct)
            : null;

        var transaction = new Transaction
        {
            UserId = user.Id,
            CategoryId = category?.Id,
            AmountMinor = amountMinor,
            Currency = request.Currency ?? user.DefaultCurrency,
            Type = request.Type,
            Note = note ?? (request.Type == TransactionType.Income ? "Доход из Telegram" : "Из Telegram"),
            OccurredAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Transactions.Add(transaction);
        await db.SaveChangesAsync(ct);

        Telemetry.TransactionsCreated.Add(1,
            new KeyValuePair<string, object?>("source", "bot"),
            new KeyValuePair<string, object?>("type", transaction.Type.ToString()));

        return new RecordedEntry(transaction, category);
    }

    /// <summary>/undo: shows the latest transaction and asks before deleting it.</summary>
    private async Task OfferUndoAsync(string botToken, long chatId, long telegramUserId, CancellationToken ct)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        var last = await BotUndo.FindLastAsync(db, telegramUserId, ct);
        if (last is null)
        {
            await SendMessageAsync(botToken, chatId, "Пока нечего отменять: записей нет.", null, ct);
            return;
        }

        var keyboard = new
        {
            inline_keyboard = new[]
            {
                new[]
                {
                    new { text = "🗑 Удалить", callback_data = BotUndo.BuildCallback(last.Id) },
                    new { text = "Оставить", callback_data = KeepData }
                }
            }
        };

        await SendMessageAsync(botToken, chatId, $"Удалить последнюю запись?\n{last.Describe()}", keyboard, ct);
    }

    private async Task UndoAsync(string botToken, long chatId, long? messageId, long telegramUserId, long transactionId, CancellationToken ct)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        var deleted = await BotUndo.DeleteAsync(db, telegramUserId, transactionId, ct);

        var text = deleted is not null
            ? $"↩️ Запись удалена:\n{deleted.Describe()}"
            : "Эта запись уже удалена.";

        await EditOrSendAsync(botToken, chatId, messageId, text, ct);
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
            $"➖ Расходы: {NotificationTexts.FormatMoney(expense, currency)}",
            $"➕ Доходы: {NotificationTexts.FormatMoney(income, currency)}"
        };

        if (topCategories.Count > 0)
        {
            lines.Add("");
            lines.Add("📂 Топ категорий:");
            lines.AddRange(topCategories.Select(c => $"  • {c.Name}: {NotificationTexts.FormatMoney(c.Total, currency)}"));
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
                   "/report — сводка за 7 дней\n" +
                   "/undo — удалить последнюю запись\n\n" +
                   "Или просто отправьте сумму с описанием, например «1500 кофе» или «+50000 зарплата», — я подберу категорию и запишу.";

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
                    new { text = "↩️ Отменить последнюю запись", callback_data = "menu_undo" }
                ],
                [
                    new { text = "📱 Открыть приложение", web_app = new { url = WebAppUrl } }
                ]
            }
        };

        return SendMessageAsync(botToken, chatId,
            "📱 Главное меню:\n\nВыберите действие или откройте приложение.", keyboard, ct);
    }

    public static string FormatAmount(decimal amount) =>
        amount.ToString("0.##", CultureInfo.InvariantCulture);

    private Task SendMessageAsync(
        string botToken,
        long chatId,
        string text,
        object? replyMarkup,
        CancellationToken ct,
        long? replyToMessageId = null) =>
        CallTelegramAsync(botToken, "sendMessage", new
        {
            chat_id = chatId,
            text,
            reply_markup = replyMarkup,
            reply_parameters = replyToMessageId is { } messageId
                ? new { message_id = messageId, allow_sending_without_reply = true }
                : null
        }, ct);

    private Task EditMessageAsync(string botToken, long chatId, long messageId, string text, object? replyMarkup, CancellationToken ct) =>
        CallTelegramAsync(botToken, "editMessageText",
            new { chat_id = chatId, message_id = messageId, text, reply_markup = replyMarkup }, ct);

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
