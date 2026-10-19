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
    IConfiguration configuration,
    BotRateLimiter rateLimiter)
{
    private const decimal MaxAmount = BotEntryParser.MaxAmount;
    private const string LegacyConfirmPrefix = "confirm:"; // buttons sent by older versions
    private const string CancelData = "cancel";
    private const string KeepData = "keep";

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
            if (!await TryPassRateLimitAsync(botToken, update, ct))
            {
                activity?.SetTag("bot.rate_limited", true);
                return false;
            }

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

    /// <summary>
    /// Drops updates from a user who sends too many. A pressed button is still
    /// answered (so its spinner stops); the user hears why once in a while.
    /// </summary>
    private async Task<bool> TryPassRateLimitAsync(string botToken, JObject update, CancellationToken ct)
    {
        var source = update["callback_query"] ?? update["message"];
        if (source?["from"]?["id"]?.Value<long>() is not { } telegramUserId ||
            rateLimiter.TryAcquire(telegramUserId, out var shouldWarn))
        {
            return true;
        }

        logger.LogWarning("Rate limit exceeded by Telegram user {TelegramUserId}", telegramUserId);

        if (update["callback_query"] is JObject callbackQuery)
            await AnswerCallbackQueryAsync(botToken, callbackQuery["id"]?.Value<string>() ?? "", ct);

        // No database round trip on this path: answer in the language of the user's Telegram app.
        var t = BotText.For(Languages.FromTelegram(source["from"]?["language_code"]?.Value<string>()));
        var chatId = source["chat"]?["id"]?.Value<long>() ?? source["message"]?["chat"]?["id"]?.Value<long>();
        if (shouldWarn && chatId is { } id)
            await SendMessageAsync(botToken, id, t.TooManyMessages, null, ct);

        return false;
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
        var languageCode = message["from"]?["language_code"]?.Value<string>();

        if (string.IsNullOrWhiteSpace(text) || chatId is null)
            return false;

        var t = await TextForAsync(telegramUserId, languageCode, ct);

        var command = text.Trim().Split(' ', '@')[0].ToLowerInvariant();

        switch (command)
        {
            case "/start":
                // t.me/<bot>?start=join_CODE arrives as "/start join_CODE".
                if (TryReadJoinCode(text, out var inviteCode) && telegramUserId.HasValue)
                    await JoinWalletAsync(botToken, t, chatId.Value, telegramUserId.Value, firstName, languageCode, inviteCode, ct);
                else
                    await SendMessageAsync(botToken, chatId.Value, t.Start(firstName), null, ct);
                return true;

            case "/expenses":
                await SendWebAppMessageAsync(botToken, chatId.Value, t.OpenAppPrompt, t.OpenAppButton, WebAppUrl, ct);
                return true;

            case "/income":
                await SendWebAppMessageAsync(botToken, chatId.Value, t.RecordIncomePrompt, t.RecordIncomeButton, WebAppUrl, ct);
                return true;

            case "/report":
                if (telegramUserId.HasValue)
                    await SendWeeklyReportAsync(botToken, t, chatId.Value, telegramUserId.Value, ct);
                return true;

            case "/menu":
                await SendMenuMessageAsync(botToken, t, chatId.Value, ct);
                return true;

            case "/undo":
                if (telegramUserId.HasValue)
                    await OfferUndoAsync(botToken, t, chatId.Value, telegramUserId.Value, ct);
                return true;

            case "/language":
                if (telegramUserId.HasValue)
                    await SwitchLanguageAsync(botToken, chatId.Value, telegramUserId.Value, firstName, languageCode, text, ct);
                return true;
        }

        if (telegramUserId.HasValue && TryParseEntry(text, out var entry))
        {
            await AskToConfirmAsync(botToken, t, chatId.Value, messageId, telegramUserId.Value, firstName, languageCode, entry, ct);
            return true;
        }

        // Looks like an attempt to record something: explain the format.
        if (text.Any(char.IsDigit))
        {
            await SendMessageAsync(botToken, chatId.Value, t.AmountNotUnderstood, null, ct);
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
        string botToken, BotText t, long chatId, long telegramUserId, string? firstName, string? languageCode, string code,
        CancellationToken ct)
    {
        var user = await GetOrCreateUserAsync(telegramUserId, firstName, languageCode, ct);

        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        var result = await WalletInvites.JoinAsync(db, user.Id, code, DateTime.UtcNow, ct);

        var text = result.Status switch
        {
            JoinWalletStatus.Joined => t.JoinedWallet(result.Wallet!.Name),
            JoinWalletStatus.AlreadyMember => t.AlreadyWalletMember(result.Wallet!.Name),
            JoinWalletStatus.Full => t.WalletFull(result.Wallet!.Name, WalletInvites.MaxMembers),
            _ => t.InviteNotFound
        };

        await SendWebAppMessageAsync(botToken, chatId, text, t.WalletsButton, WebAppUrl, ct);
    }

    /// <summary>
    /// /language switches between English and Russian; "/language en" or "/language ru"
    /// picks one. Saved in the profile, so the Mini App and notifications follow.
    /// </summary>
    private async Task SwitchLanguageAsync(
        string botToken, long chatId, long telegramUserId, string? firstName, string? languageCode, string text, CancellationToken ct)
    {
        var user = await GetOrCreateUserAsync(telegramUserId, firstName, languageCode, ct);
        var argument = text.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
        var language = Languages.TryNormalize(argument, out var requested)
            ? requested
            : user.Language == Languages.Russian ? Languages.English : Languages.Russian;

        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();
        await db.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Language, language), ct);

        await SendMessageAsync(botToken, chatId, BotText.For(language).LanguageChanged, null, ct);
    }

    /// <summary>A bare amount ("1 500,50") or a free-form entry ("1500 coffee", "+20$ refund").</summary>
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
        var firstName = sender?["first_name"]?.Value<string>();
        var languageCode = sender?["language_code"]?.Value<string>();

        try
        {
            if (chatId is null)
                return false;

            var t = await TextForAsync(telegramUserId, languageCode, ct);

            switch (data)
            {
                case CancelData:
                case "cancel_expense":
                    await EditOrSendAsync(botToken, chatId.Value, messageId, t.Cancelled, ct);
                    return true;

                case KeepData:
                    await EditOrSendAsync(botToken, chatId.Value, messageId, t.Kept, ct);
                    return true;

                case "menu_stats":
                    await SendWeeklyReportAsync(botToken, t, chatId.Value, telegramUserId ?? 0, ct);
                    return true;

                case "menu_undo":
                    if (telegramUserId.HasValue)
                        await OfferUndoAsync(botToken, t, chatId.Value, telegramUserId.Value, ct);
                    return true;

                case "menu_income":
                case "menu_expense":
                    await SendMessageAsync(botToken, chatId.Value, t.EntryHint, null, ct);
                    return true;

                case "menu_goals":
                case "menu_categories":
                    await SendWebAppMessageAsync(botToken, chatId.Value, t.GoalsInApp, t.OpenAppButton, WebAppUrl, ct);
                    return true;
            }

            if (telegramUserId is not null && BotUndo.TryParseCallback(data, out var undoId))
            {
                await UndoAsync(botToken, t, chatId.Value, messageId, telegramUserId.Value, undoId, ct);
                return true;
            }

            if (telegramUserId is not null && RecordCallback.TryParseTypeStep(data, out var typeStep))
            {
                var stepUser = await GetOrCreateUserAsync(telegramUserId.Value, firstName, languageCode, ct);
                await ShowCategoryPickerAsync(botToken, t, chatId.Value, stepUser, typeStep,
                    ReadNoteFromQuestion(callbackQuery), messageId, null, ct);
                return true;
            }

            if (telegramUserId is null || !TryParseRecord(data, out var request))
                return false;

            var user = await GetOrCreateUserAsync(telegramUserId.Value, firstName, languageCode, ct);
            var note = ReadNoteFromQuestion(callbackQuery);
            var recorded = await RecordTransactionAsync(t, user, request, note, ct);

            if (recorded is not { } entry)
            {
                await EditOrSendAsync(botToken, chatId.Value, messageId, t.RecordFailed, ct);
                return true;
            }

            var result = t.Recorded(
                request.Type,
                $"{FormatAmount(request.Amount)} {entry.Transaction.Currency}",
                entry.Category is { } category ? $"{category.Icon} {category.Name}" : null,
                note);

            // The record buttons are replaced with a single "undo", so the same
            // transaction can't be recorded twice by tapping again.
            var undoKeyboard = new
            {
                inline_keyboard = new[]
                {
                    new[] { new { text = t.UndoButton, callback_data = BotUndo.BuildCallback(entry.Transaction.Id) } }
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
        BotText t,
        long chatId,
        long? replyToMessageId,
        long telegramUserId,
        string? firstName,
        string? languageCode,
        BotEntry entry,
        CancellationToken ct)
    {
        var user = await GetOrCreateUserAsync(telegramUserId, firstName, languageCode, ct);
        var currency = entry.Currency ?? user.DefaultCurrency;
        var guess = await GuessCategoryAsync(user.Id, entry.Note, ct);

        if (entry.Type is { } knownType)
        {
            var request = new RecordCallback(knownType, entry.Amount, currency, guess?.Id);
            await ShowCategoryPickerAsync(botToken, t, chatId, user, request, entry.Note, null, replyToMessageId, ct);
            return;
        }

        string TypeStep(TransactionType type) =>
            new RecordCallback(type, entry.Amount, currency, guess?.Id).BuildTypeStep();

        var keyboard = new
        {
            inline_keyboard = new object[][]
            {
                [
                    new { text = t.ExpenseButton, callback_data = TypeStep(TransactionType.Expense) },
                    new { text = t.IncomeButton, callback_data = TypeStep(TransactionType.Income) }
                ],
                [
                    new { text = t.CancelButton, callback_data = CancelData }
                ]
            }
        };

        var text = DescribeEntry(entry.Amount, currency, entry.Note, guess) + "\n\n" + t.ExpenseOrIncome;

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
        BotText t,
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
                new { text = t.NoCategoryButton, callback_data = Record(null) },
                new { text = t.CancelButton, callback_data = CancelData }
            })
            .ToArray();

        var text = DescribeEntry(request.Amount, request.Currency ?? user.DefaultCurrency, note, guess) +
                   "\n\n" + t.ChooseCategory(request.Type);
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

    /// <summary>Texts in the user's saved language, or in their Telegram app's language before the first entry.</summary>
    private async Task<BotText> TextForAsync(long? telegramUserId, string? languageCode, CancellationToken ct)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        var saved = telegramUserId is { } id
            ? await db.Users.Where(u => u.TelegramUserId == id).Select(u => u.Language).FirstOrDefaultAsync(ct)
            : null;

        return BotText.For(saved ?? Languages.FromTelegram(languageCode));
    }

    private async Task<User> GetOrCreateUserAsync(long telegramUserId, string? firstName, string? languageCode, CancellationToken ct)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, ct);

        if (user is not null)
            return user;

        user = UserFactory.Create(telegramUserId, firstName, Languages.FromTelegram(languageCode));
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

    private async Task<RecordedEntry?> RecordTransactionAsync(BotText t, User user, RecordCallback request, string? note, CancellationToken ct)
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
            Note = note ?? t.DefaultNote(request.Type),
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
    private async Task OfferUndoAsync(string botToken, BotText t, long chatId, long telegramUserId, CancellationToken ct)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        var last = await BotUndo.FindLastAsync(db, telegramUserId, ct);
        if (last is null)
        {
            await SendMessageAsync(botToken, chatId, t.NothingToUndo, null, ct);
            return;
        }

        var keyboard = new
        {
            inline_keyboard = new[]
            {
                new[]
                {
                    new { text = t.DeleteButton, callback_data = BotUndo.BuildCallback(last.Id) },
                    new { text = t.KeepButton, callback_data = KeepData }
                }
            }
        };

        await SendMessageAsync(botToken, chatId, t.ConfirmUndo(last.Describe(t)), keyboard, ct);
    }

    private async Task UndoAsync(string botToken, BotText t, long chatId, long? messageId, long telegramUserId, long transactionId, CancellationToken ct)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        var deleted = await BotUndo.DeleteAsync(db, telegramUserId, transactionId, ct);

        var text = deleted is not null
            ? t.Undone(deleted.Describe(t))
            : t.AlreadyDeleted;

        await EditOrSendAsync(botToken, chatId, messageId, text, ct);
    }

    private async Task SendWeeklyReportAsync(string botToken, BotText t, long chatId, long telegramUserId, CancellationToken ct)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpensesDbContext>();

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, ct);

        if (user is null)
        {
            await SendMessageAsync(botToken, chatId, t.NoDataYet, null, ct);
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
            .GroupBy(x => x.Category != null ? x.Category.Name : null)
            .Select(g => new { Name = g.Key, Total = g.Sum(t => t.AmountMinor) })
            .OrderByDescending(x => x.Total)
            .Take(5)
            .ToListAsync(ct);

        var expense = totals.FirstOrDefault(x => x.Type == TransactionType.Expense)?.Total ?? 0;
        var income = totals.FirstOrDefault(x => x.Type == TransactionType.Income)?.Total ?? 0;
        var currency = user.DefaultCurrency;

        var lines = new List<string>
        {
            t.ReportTitle,
            "",
            t.ReportExpenses(t.Money(expense, currency)),
            t.ReportIncome(t.Money(income, currency))
        };

        if (topCategories.Count > 0)
        {
            lines.Add("");
            lines.Add(t.TopCategories);
            lines.AddRange(topCategories.Select(c => $"  • {c.Name ?? t.Shared.NoCategory}: {t.Money(c.Total, currency)}"));
        }

        lines.Add("");
        lines.Add(t.ReportFooter(currency));

        await SendMessageAsync(botToken, chatId, string.Join("\n", lines), null, ct);
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

    private Task SendMenuMessageAsync(string botToken, BotText t, long chatId, CancellationToken ct)
    {
        var keyboard = new
        {
            inline_keyboard = new object[][]
            {
                [
                    new { text = t.MenuSummary, callback_data = "menu_stats" },
                    new { text = t.MenuRecord, callback_data = "menu_expense" }
                ],
                [
                    new { text = t.MenuGoals, callback_data = "menu_goals" },
                    new { text = t.MenuCategories, callback_data = "menu_categories" }
                ],
                [
                    new { text = t.MenuUndo, callback_data = "menu_undo" }
                ],
                [
                    new { text = t.MenuOpenApp, web_app = new { url = WebAppUrl } }
                ]
            }
        };

        return SendMessageAsync(botToken, chatId, t.MenuTitle, keyboard, ct);
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
