using ExpensesBot.Data;
using ExpensesBot.Data.Entities;

namespace ExpensesBot.Worker;

/// <summary>
/// The bot's chat texts in English and Russian side by side (<see cref="Pick"/>
/// takes both, so a missing translation doesn't compile). Texts shared with the
/// API, such as notifications, live in <see cref="MessageText"/>.
/// </summary>
public sealed class BotText
{
    public static readonly BotText En = new(MessageText.En);
    public static readonly BotText Ru = new(MessageText.Ru);

    public static BotText For(string? language) => language == Languages.Russian ? Ru : En;

    private BotText(MessageText shared) => Shared = shared;

    /// <summary>Texts shared with the API in the same language.</summary>
    public MessageText Shared { get; }

    public string Language => Shared.Language;

    private string Pick(string en, string ru) => Shared.IsRussian ? ru : en;

    public string Money(long amountMinor, string currency) => Shared.Money(amountMinor, currency);

    public string Date(DateTime value) =>
        value.ToString(Shared.IsRussian ? "dd.MM.yyyy" : "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public string EntryHint => Pick(
        "✏️ Send an amount and, if you like, a description:\n" +
        "• 1500 coffee\n" +
        "• +50000 salary — plus means income\n" +
        "• 20$ taxi or taxi 700 rub — another currency\n" +
        "• 1.5k groceries — thousands",
        "✏️ Отправьте сумму и, если хотите, описание:\n" +
        "• 1500 кофе\n" +
        "• +50000 зарплата — плюс значит доход\n" +
        "• 20$ такси или такси 700 руб — другая валюта\n" +
        "• 1,5к продукты — тысячи");

    public string AmountNotUnderstood => Pick("I didn't get the amount. ", "Не понял сумму. ") + EntryHint;

    public string TooManyMessages => Pick("⏳ Too many messages in a row — please wait a little.", "⏳ Слишком много сообщений подряд — подождите немного.");

    public string Start(string? firstName) => Pick(
        $"Hi, {firstName ?? "there"}! 👋\n\n" +
        "I'm a personal finance tracker. Here's what I can do:\n\n" +
        "/menu — main menu\n" +
        "/expenses — open the app\n" +
        "/report — summary for 7 days\n" +
        "/undo — delete the latest entry\n" +
        "/language — Русский язык\n\n" +
        "Or just send an amount with a description, like “1500 coffee” or “+50000 salary”, and I'll pick a category and record it.",
        $"Привет, {firstName ?? "друг"}! 👋\n\n" +
        "Я — трекер личных финансов. Вот что я умею:\n\n" +
        "/menu — главное меню\n" +
        "/expenses — открыть приложение\n" +
        "/report — сводка за 7 дней\n" +
        "/undo — удалить последнюю запись\n" +
        "/language — English\n\n" +
        "Или просто отправьте сумму с описанием, например «1500 кофе» или «+50000 зарплата», — я подберу категорию и запишу.");

    public string LanguageChanged => Pick(
        "🌐 Language: English. The app and notifications use it too.\nПереключить на русский: /language",
        "🌐 Язык: русский. Приложение и уведомления тоже будут на нём.\nSwitch to English: /language");

    // Opening the Mini App

    public string OpenAppPrompt => Pick("📊 Open the finance app:", "📊 Откройте приложение для учёта финансов:");
    public string OpenAppButton => Pick("Open ExpensesBot", "Открыть ExpensesBot");
    public string RecordIncomePrompt => Pick("📈 Open the app to record income:", "📈 Откройте приложение, чтобы записать доход:");
    public string RecordIncomeButton => Pick("📈 Record income", "📈 Записать доход");
    public string GoalsInApp => Pick("Goals and categories are set up in the app:", "Цели и категории настраиваются в приложении:");

    // Menu

    public string MenuTitle => Pick("📱 Main menu:\n\nChoose an action or open the app.", "📱 Главное меню:\n\nВыберите действие или откройте приложение.");
    public string MenuSummary => Pick("📊 Summary", "📊 Сводка");
    public string MenuRecord => Pick("✏️ Record", "✏️ Записать");
    public string MenuGoals => Pick("🎯 Goals", "🎯 Цели");
    public string MenuCategories => Pick("📁 Categories", "📁 Категории");
    public string MenuUndo => Pick("↩️ Undo the latest entry", "↩️ Отменить последнюю запись");
    public string MenuOpenApp => Pick("📱 Open the app", "📱 Открыть приложение");

    // Recording

    public string ExpenseOrIncome => Pick("Is this an expense or income?", "Это расход или доход?");
    public string ExpenseButton => Pick("➖ Expense", "➖ Расход");
    public string IncomeButton => Pick("➕ Income", "➕ Доход");
    public string CancelButton => Pick("❌ Cancel", "❌ Отмена");
    public string NoCategoryButton => Pick("🚫 No category", "🚫 Без категории");
    public string ChooseCategory(TransactionType type) =>
        (type == TransactionType.Income ? IncomeButton : ExpenseButton) + Pick(" — choose a category:", " — выберите категорию:");
    public string Cancelled => Pick("Cancelled.", "Отменено.");
    public string RecordFailed => Pick("Couldn't record it. Open the app with /expenses and try again.",
        "Не удалось записать. Откройте приложение /expenses и попробуйте снова.");

    /// <summary>"✅ Expense 1500 AMD recorded · 🛒 Groceries · coffee."</summary>
    public string Recorded(TransactionType type, string amount, string? category, string? note) =>
        Pick(type == TransactionType.Income ? "✅ Income " : "✅ Expense ", type == TransactionType.Income ? "✅ Доход " : "✅ Расход ") +
        amount + Pick(" recorded", " записан") +
        (category is not null ? $" · {category}" : "") +
        (note is not null ? $" · {note}" : "") +
        Pick(".\n\nDetailed statistics are in the app: /expenses", ".\n\nПодробная статистика — в приложении: /expenses");

    /// <summary>Note of an entry recorded without a description.</summary>
    public string DefaultNote(TransactionType type) =>
        type == TransactionType.Income ? Pick("Income from Telegram", "Доход из Telegram") : Pick("From Telegram", "Из Telegram");

    // Undo

    public string UndoButton => Pick("↩️ Undo", "↩️ Отменить");
    public string DeleteButton => Pick("🗑 Delete", "🗑 Удалить");
    public string KeepButton => Pick("Keep", "Оставить");
    public string NothingToUndo => Pick("Nothing to undo yet: there are no entries.", "Пока нечего отменять: записей нет.");
    public string ConfirmUndo(string entry) => Pick($"Delete the latest entry?\n{entry}", $"Удалить последнюю запись?\n{entry}");
    public string Undone(string entry) => Pick($"↩️ Entry deleted:\n{entry}", $"↩️ Запись удалена:\n{entry}");
    public string AlreadyDeleted => Pick("This entry is already deleted.", "Эта запись уже удалена.");
    public string Kept => Pick("OK, the entry stays.", "Хорошо, запись осталась.");

    // Report

    public string NoDataYet => Pick("No data yet. Send an expense amount, for example: 1500", "Пока нет данных. Отправьте сумму расхода, например: 1500");
    public string ReportTitle => Pick("📊 Last 7 days", "📊 Последние 7 дней");
    public string ReportExpenses(string amount) => Pick($"➖ Expenses: {amount}", $"➖ Расходы: {amount}");
    public string ReportIncome(string amount) => Pick($"➕ Income: {amount}", $"➕ Доходы: {amount}");
    public string TopCategories => Pick("📂 Top categories:", "📂 Топ категорий:");
    public string ReportFooter(string currency) => Pick(
        $"Only transactions in {currency} are counted. Full statistics — /expenses",
        $"Учитываются операции в {currency}. Полная статистика — /expenses");

    // Shared wallets

    public string WalletsButton => Pick("👥 Open wallets", "👥 Открыть кошельки");
    public string JoinedWallet(string wallet) => Pick(
        $"👥 You joined the shared wallet “{wallet}”.\n\nEvery member sees the transactions recorded into it. Pick the wallet when recording and see the shared summary in the app.",
        $"👥 Вы присоединились к общему кошельку «{wallet}».\n\nОперации, записанные в него, видят все участники. Выбрать кошелёк при записи и посмотреть общую сводку можно в приложении.");
    public string AlreadyWalletMember(string wallet) => Pick($"You're already a member of “{wallet}”.", $"Вы уже участник кошелька «{wallet}».");
    public string WalletFull(string wallet, int max) => Pick(
        $"“{wallet}” already has {max} members — no more can be added.",
        $"В кошельке «{wallet}» уже {max} участников — больше добавить нельзя.");
    public string InviteNotFound => Pick("Invitation not found: the owner may have changed the code. Ask for a new link.",
        "Приглашение не найдено: возможно, владелец сменил код. Попросите новую ссылку.");
}
