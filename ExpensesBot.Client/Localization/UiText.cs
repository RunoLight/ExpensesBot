namespace ExpensesBot.Client.Localization;

/// <summary>
/// Every user-visible string of the Mini App, in English and Russian side by side.
/// <see cref="Pick"/> takes both translations, so a missing one is a compile error
/// rather than a blank label. Components receive the current instance as a
/// cascading value (see <see cref="LanguageState"/>); English is the default.
/// </summary>
public sealed class UiText
{
    public const string English = "en";
    public const string Russian = "ru";

    public static readonly UiText En = new(English);
    public static readonly UiText Ru = new(Russian);

    public static UiText For(string? language) => language == Russian ? Ru : En;

    /// <summary>Supported language for a Telegram language_code ("ru", "en-US", …); English otherwise.</summary>
    public static string LanguageFromTelegram(string? code) =>
        code?.StartsWith(Russian, StringComparison.OrdinalIgnoreCase) == true ? Russian : English;

    private UiText(string language) => Language = language;

    public string Language { get; }

    public bool IsRussian => Language == Russian;

    private string Pick(string en, string ru) => IsRussian ? ru : en;

    /// <summary>Russian has three plural forms (1 операция, 2 операции, 5 операций), English two.</summary>
    private string Count(long n, string enOne, string enMany, string ruOne, string ruFew, string ruMany)
    {
        if (!IsRussian)
            return $"{n} {(n == 1 ? enOne : enMany)}";

        var word = (n % 100) is >= 11 and <= 14 ? ruMany
            : (n % 10) switch { 1 => ruOne, >= 2 and <= 4 => ruFew, _ => ruMany };
        return $"{n} {word}";
    }

    // Dates

    private static readonly string[] MonthsEn = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    private static readonly string[] MonthsRu = ["янв", "фев", "мар", "апр", "мая", "июн", "июл", "авг", "сен", "окт", "ноя", "дек"];
    private static readonly string[] MonthTitlesRu = ["Янв", "Фев", "Мар", "Апр", "Май", "Июн", "Июл", "Авг", "Сен", "Окт", "Ноя", "Дек"];

    /// <summary>"16 Oct" / "16 окт".</summary>
    public string DayMonth(DateTime date) => $"{date.Day} {(IsRussian ? MonthsRu : MonthsEn)[date.Month - 1]}";

    /// <summary>Chart label for a month: "Oct 2026" / "Окт 2026".</summary>
    public string MonthYear(int year, int month) => $"{(IsRussian ? MonthTitlesRu : MonthsEn)[month - 1]} {year}";

    /// <summary>"16 Oct 2026" / "16.10.2026".</summary>
    public string Date(DateTime date) =>
        IsRussian ? date.ToString("dd.MM.yyyy") : $"{DayMonth(date)} {date.Year}";

    public string DateTime(DateTime value) => $"{Date(value)} {value:HH:mm}";

    /// <summary>A date range inside one year: "1 Oct – 16 Oct 2026".</summary>
    public string DateRange(DateTime from, DateTime to) =>
        IsRussian
            ? $"{from:dd.MM} – {to:dd.MM.yyyy}"
            : $"{DayMonth(from)} – {DayMonth(to)} {to.Year}";

    // App shell

    public string AppTitle => Pick("My finances", "Мои финансы");
    public string AppEyebrow => Pick("Personal finance", "Личные финансы");
    public string CurrencyTitle => Pick("Main currency: totals are shown and notifications are sent in it",
        "Основная валюта: в ней показываются итоги и приходят уведомления");
    public string Help => Pick("How it works", "Как это работает");
    public string LightTheme => Pick("Light theme", "Светлая тема");
    public string DarkTheme => Pick("Dark theme", "Тёмная тема");
    public string ImportCsv => Pick("Import CSV", "Импорт CSV");
    public string DeleteAccount => Pick("Delete account", "Удалить аккаунт");
    public string SwitchLanguage => Pick("Русский язык", "English");

    /// <summary>Label of the language button: the language it switches to.</summary>
    public string OtherLanguageCode => Pick("RU", "EN");
    public string Loading => Pick("Loading…", "Загружаем данные...");
    public string LoadingShort => Pick("Loading…", "Загружаем...");
    public string NotFoundTitle => Pick("Page not found", "Страница не найдена");
    public string NotFoundPageTitle => Pick("Not found", "Не найдено");
    public string BackHome => Pick("Back to the main page", "Вернуться на главную");

    // Common

    public string Close => Pick("Close", "Закрыть");
    public string Cancel => Pick("Cancel", "Отмена");
    public string Save => Pick("Save", "Сохранить");
    public string Add => Pick("Add", "Добавить");
    public string Edit => Pick("Edit", "Изменить");
    public string Delete => Pick("Delete", "Удалить");
    public string Done => Pick("Done", "Готово");
    public string GotIt => Pick("Got it", "Понятно");
    public string Name => Pick("Name", "Название");
    public string Amount => Pick("Amount", "Сумма");
    public string Currency => Pick("Currency", "Валюта");
    public string Category => Pick("Category", "Категория");
    public string Note => Pick("Note", "Комментарий");
    public string TransactionDate => Pick("Date", "Дата");
    public string NoCategory => Pick("No category", "Без категории");
    public string Expense => Pick("Expense", "Расход");
    public string Income => Pick("Income", "Доход");
    public string Empty => Pick("Nothing here yet", "Пока пусто");
    public string CannotBeUndone => Pick("This cannot be undone.", "Это действие нельзя отменить.");

    // Summary

    public string Period => Pick("Period", "Период");
    public string ThisMonth => Pick("This month", "Этот месяц");
    public string Last30Days => Pick("30 days", "30 дней");
    public string AllTime => Pick("All time", "Всё время");
    public string AllTimeRange => Pick("all time", "за всё время");
    public string Balance => Pick("Balance", "Баланс");
    public string IncomeTotal => Pick("📈 Income", "📈 Доходы");
    public string ExpenseTotal => Pick("📉 Expenses", "📉 Расходы");
    public string AveragePerDay => Pick("Average per day", "В среднем в день");
    public string MonthlyProjection => Pick("Projected spending this month", "Прогноз расходов на месяц");
    public string Last30DaysSpending => Pick("Spending over the last 30 days", "Расходы за последние 30 дней");
    public string ApproximateRates => Pick("Exchange rates are approximate: fresh rates haven't been loaded yet.",
        "Курсы валют приблизительные — свежие курсы ещё не загружены.");
    public string NoOperations => Pick("no transactions", "операций нет");
    public string Operations(long n) => Count(n, "transaction", "transactions", "операция", "операции", "операций");

    // Breakdown

    public string ByPeriod => Pick("By period", "По периодам");
    public string ByCategory => Pick("By category", "По категориям");
    public string SpendingStructure => Pick("Spending structure", "Структура расходов");
    public string SpendingChart => Pick("Spending by category chart", "Диаграмма расходов по категориям");

    // Categories

    public string Categories => Pick("Categories", "Категории");
    public string NewCategory => Pick("New category", "Новая категория");
    public string ManageCategories => Pick("Manage categories", "Управление категориями");
    public string Icon => Pick("Icon", "Иконка");
    public string Unarchive => Pick("Restore from archive", "Вернуть из архива");
    public string ArchiveHint => Pick("Archive — hide from forms", "Архивировать — скрыть из форм");
    public string Archived => Pick("archived", "архив");
    public string ConfirmArchiveCategory(string name) => Pick(
        $"Archive the category “{name}”? It disappears from forms but stays in the history.",
        $"Архивировать категорию «{name}»? Она пропадёт из форм, но останется в истории.");
    public string ConfirmUnarchiveCategory(string name) => Pick(
        $"Restore the category “{name}” from the archive?",
        $"Вернуть категорию «{name}» из архива?");
    public string ConfirmDeleteCategory(string name) => Pick(
        $"Delete the category “{name}”? Its transactions stay, without a category.",
        $"Удалить категорию «{name}»? Операции останутся, но без категории.");

    // Recurring

    public string Recurring => Pick("Recurring", "Повторяющиеся");
    public string NewRecurring => Pick("New recurring transaction", "Новая повторяющаяся операция");
    public string RecurringNotePlaceholder => Pick("For example, rent or salary", "Например, аренда или зарплата");
    public string Frequency => Pick("Frequency", "Периодичность");
    public string Daily => Pick("Daily", "Ежедневно");
    public string Weekly => Pick("Weekly", "Еженедельно");
    public string Monthly => Pick("Monthly", "Ежемесячно");
    public string Yearly => Pick("Yearly", "Ежегодно");
    public string RunNow => Pick("Record now", "Выполнить сейчас");
    public string ConfirmRunRecurring(string name) => Pick(
        $"Record “{name}” now? The next automatic payment will be counted from today.",
        $"Записать «{name}» сейчас? Следующий автоматический платёж будет отсчитываться от сегодняшнего дня.");
    public string ConfirmDeleteRecurring(string name) => Pick(
        $"Delete the recurring transaction “{name}”? Transactions created so far stay.",
        $"Удалить повторяющуюся операцию «{name}»? Уже созданные операции останутся.");

    public string PatternLabel(string pattern) => pattern switch
    {
        "Daily" => Daily,
        "Weekly" => Weekly,
        "Monthly" => Monthly,
        "Yearly" => Yearly,
        _ => pattern
    };

    // Goals

    public string Goals => Pick("Goals", "Цели");
    public string NewGoal => Pick("New goal", "Новая цель");
    public string Until(DateTime date) => Pick($"by {Date(date)}", $"до {Date(date)}");
    public string AmountIn(string currency) => Pick($"Amount, {currency}", $"Сумма, {currency}");
    public string Contribute => Pick("➕ Add", "➕ Внести");
    public string GoalName => Pick("Goal name", "Название цели");
    public string GoalNamePlaceholder => Pick("For example, a new phone", "Например, новый телефон");
    public string TargetAmount => Pick("Target amount", "Целевая сумма");
    public string NewGoalCategory => Pick("New category for the goal", "Новая категория для цели");
    public string GoalFixedFields => Pick("The goal's currency and category can't change: contributions are recorded in them.",
        "Валюта и категория цели не меняются: уже внесённые взносы записаны в них.");
    public string GoalDate => Pick("Target date (optional)", "Дата цели (необязательно)");
    public string CreateGoal => Pick("Create goal", "Создать цель");
    public string ConfirmContribute(string amount, string goal) => Pick(
        $"Add {amount} to the goal “{goal}”? The contribution is recorded as income in the goal's category.",
        $"Внести {amount} в цель «{goal}»? Взнос будет записан как доход в категории цели.");
    public string ConfirmDeleteGoal(string name) => Pick(
        $"Delete the goal “{name}”? Contributions made so far stay in the history.",
        $"Удалить цель «{name}»? Уже внесённые взносы останутся в истории.");

    // Budgets

    public string Budgets => Pick("Budgets", "Бюджеты");
    public string NewBudget => Pick("New budget", "Новый бюджет");
    public string OverallBudget => Pick("Overall", "Общий");
    public string OverallBudgetOption => Pick("Overall (all categories)", "Общий (без категории)");
    public string Exceeded => Pick("⚠️ Over budget!", "⚠️ Превышен!");
    public string Limit => Pick("Limit", "Лимит");
    public string From => Pick("From", "С");
    public string To => Pick("To", "По");
    public string CreateBudget => Pick("Create budget", "Создать бюджет");
    public string ConfirmDeleteBudget(string name) => Pick($"Delete the budget “{name}”?", $"Удалить бюджет «{name}»?");

    // Transaction form and history

    public string RecordInto => Pick("Record into", "Куда записать");
    public string PersonalTransaction => Pick("👤 Personal transaction", "👤 Личная операция");
    public string NotePlaceholder => Pick("For example, lunch", "Например, обед");
    public string Saving => Pick("Saving…", "Сохраняем...");
    public string AddIncome => Pick("Add income", "Добавить доход");
    public string AddExpense => Pick("Add expense", "Добавить расход");
    public string EditIncome => Pick("Edit income", "Редактировать доход");
    public string EditExpense => Pick("Edit expense", "Редактировать расход");
    public string History => Pick("History", "История");
    public string Refresh => Pick("Refresh", "Обновить");
    public string HistoryEmpty => Pick("Add your first transaction with the form above or send an amount to the bot.",
        "Добавьте первую операцию в форме выше или отправьте сумму боту.");
    public string NothingFound => Pick("Nothing found", "Ничего не найдено");
    public string ChangeSearch => Pick("Try different search settings.", "Попробуйте изменить параметры поиска.");
    public string SharedWallet => Pick("shared wallet", "общий кошелёк");
    public string ConfirmDeleteTransaction(string name) => Pick(
        $"Delete the transaction “{name}”? This cannot be undone.",
        $"Удалить операцию «{name}»? Это действие нельзя отменить.");

    // Search and filters

    public string SearchPlaceholder => Pick("Search notes or categories…", "Поиск по заметке или категории...");
    public string AllCategories => Pick("All categories", "Все категории");
    public string TagsLabel => Pick("Tags:", "Теги:");
    public string ClearTags => Pick("Clear tags", "Сбросить теги");
    public string Clear => Pick("Clear", "Сбросить");
    public string ExportCsv => Pick("📤 Export CSV", "📤 Экспорт CSV");
    public string NewestFirst => Pick("Newest first", "Сначала новые");
    public string OldestFirst => Pick("Oldest first", "Сначала старые");
    public string AmountDesc => Pick("Amount ↓", "По сумме ↓");
    public string AmountAsc => Pick("Amount ↑", "По сумме ↑");
    public string CategoryAz => Pick("Category A–Z", "По категории А-Я");
    public string CategoryZa => Pick("Category Z–A", "По категории Я-А");

    // Tags

    public string Tags => Pick("Tags", "Теги");
    public string NewTag => Pick("New tag", "Новый тег");
    public string TagPlaceholder => Pick("For example, vacation", "Например, отпуск");
    public string ChangeColor => Pick("Change colour", "Сменить цвет");
    public string AddTag => Pick("Add tag", "Добавить тег");
    public string SaveTag => Pick("Save tag", "Сохранить тег");
    public string EditTag => Pick("Edit tag", "Изменить тег");
    public string DeleteTag => Pick("Delete tag", "Удалить тег");
    public string ConfirmDeleteTag(string name) => Pick(
        $"Delete the tag “{name}”? This cannot be undone.",
        $"Удалить тег «{name}»? Это действие нельзя отменить.");

    // Shared wallets

    public string Wallets => Pick("Shared wallets", "Общие кошельки");
    public string CreateWallet => Pick("Create wallet", "Создать кошелёк");
    public string JoinByCode => Pick("Join with a code", "Присоединиться по коду");
    public string WalletNamePlaceholder => Pick("For example, family", "Например, семья");
    public string InviteCode => Pick("Invite code", "Код приглашения");
    public string Join => Pick("Join", "Присоединиться");
    public string WalletsIntro => Pick(
        "A shared wallet is for a family, a couple or flatmates: every member sees the transactions recorded into it, and the summary shows who spent what. Create a wallet (+) or join with a code (🔑).",
        "Общий кошелёк — для семьи, пары или соседей: операции, записанные в него, видят все участники, а сводка показывает, кто сколько потратил. Создайте кошелёк (+) или войдите по коду (🔑).");
    public string Members(int n) => Count(n, "member", "members", "участник", "участника", "участников");
    public string YouOwn => Pick("you own it", "вы владелец");
    public string YouAreMember => Pick("you're a member", "вы участник");
    public string Hide => Pick("Hide", "Скрыть");
    public string Open => Pick("Open", "Открыть");
    public string ThisMonthColon => Pick("This month:", "Этот месяц:");
    public string WalletApproximateRates => Pick("Rates are approximate: no fresh central bank rates yet.",
        "Курсы приблизительные: свежих курсов ЦБ пока нет.");
    public string MembersTitle => Pick("Members", "Участники");
    public string YouSuffix => Pick(" (you)", " (вы)");
    public string OwnerSuffix => Pick(" · owner", " · владелец");
    public string RemoveMember => Pick("Remove", "Исключить");
    public string ExpensesByCategory => Pick("Spending by category", "Расходы по категориям");
    public string Invitation => Pick("Invitation", "Приглашение");
    public string CodeLabel => Pick("Code:", "Код:");
    public string NewCodeHint => Pick("Change the code — old links stop working", "Сменить код — старые ссылки перестанут работать");
    public string NewCode => Pick("🔄 New code", "🔄 Новый код");
    public string InviteLinkLabel => Pick("Invite link:", "Ссылка для приглашения:");
    public string SendCodeBefore => Pick("Send the code: it's entered here (🔑) or sent to the bot as", "Отправьте код: его вводят здесь (🔑) или пишут боту");
    public string LatestTransactions => Pick("Latest transactions", "Последние операции");
    public string WalletEmpty => Pick("Nothing here yet. To record a transaction here, pick the wallet in the add form.",
        "Пока пусто. Чтобы записать операцию сюда, выберите кошелёк в форме добавления.");
    public string DeleteWallet => Pick("Delete wallet", "Удалить кошелёк");
    public string LeaveWallet => Pick("Leave wallet", "Выйти из кошелька");
    public string ConfirmRegenerate(string wallet) => Pick(
        $"Change the code of “{wallet}”? Old invitations stop working.",
        $"Сменить код кошелька «{wallet}»? Старые приглашения перестанут работать.");
    public string ConfirmRemoveMember(string member, string wallet) => Pick(
        $"Remove {member} from “{wallet}”? Their transactions stay with them as personal ones.",
        $"Исключить {member} из «{wallet}»? Его операции останутся у него как личные.");
    public string ConfirmLeave(string wallet) => Pick(
        $"Leave “{wallet}”? Your transactions in it become personal.",
        $"Выйти из «{wallet}»? Ваши операции в нём станут личными.");
    public string ConfirmDeleteWallet(string wallet) => Pick(
        $"Delete the wallet “{wallet}”? Members keep their transactions as personal ones.",
        $"Удалить кошелёк «{wallet}»? Операции участников сохранятся у них как личные.");

    // Import

    public string ImportTitle => Pick("📥 Import CSV", "📥 Импорт CSV");
    public string ImportIntro => Pick("Upload a CSV file in the same format as the export:", "Загрузите CSV-файл в том же формате, что и при экспорте:");
    public string ImportDateRule => Pick("Date", "Дата");
    public string ImportDateFormat => Pick("(UTC) or", "(UTC) или");
    public string ImportTypeRule => Pick("Type", "Тип");
    public string ImportTypeValues => Pick("“Expense” or “Income”", "«Расход» или «Доход»");
    public string ImportCategoryValues => Pick("the name of an existing category; unknown ones are imported without a category",
        "название существующей категории; неизвестные импортируются без категории");
    public string ImportAmountValues => Pick("a number with a dot, for example 1500.50", "число с точкой, например 1500.50");
    public string ImportNoteValues => Pick("optional", "необязательно");
    public string Importing => Pick("Importing…", "Импорт...");
    public string RowsProgress(int done, int total) => Pick($"{done} of {total} rows", $"{done} из {total} строк");
    public string ImportedCount => Pick("✅ Transactions added:", "✅ Добавлено операций:");
    public string SkippedCount => Pick("Rows skipped because of errors:", "Пропущено строк с ошибками:");
    public string Upload => Pick("Upload", "Загрузить");
    public string ConfirmImport => Pick("The import creates many transactions at once. Continue?", "Импорт создаст сразу много операций. Продолжить?");
    public string CannotReadFile(string reason) => Pick($"Couldn't read the file: {reason}", $"Не удалось прочитать файл: {reason}");
    public string NoRows => Pick("The file has no transaction rows.", "В файле нет строк с операциями.");
    public string NothingImported => Pick("No transaction could be imported. Check the file format.",
        "Не удалось импортировать ни одной операции. Проверьте формат файла.");

    /// <summary>Header and two example rows; the header matches what the export writes in this language.</summary>
    public string SampleCsv => Pick(
        "Date (UTC),Type,Category,Amount,Currency,Note\n" +
        "2026-01-15 14:30,Expense,Groceries,1500.50,AMD,Vegetables\n" +
        "2026-01-16 09:00,Income,Salary,500000.00,AMD,January",
        "Дата (UTC),Тип,Категория,Сумма,Валюта,Комментарий\n" +
        "2026-01-15 14:30,Расход,Продукты,1500.50,AMD,Овощи\n" +
        "2026-01-16 09:00,Доход,Зарплата,500000.00,AMD,Январь");

    // Account

    public string DeleteAccountTitle => Pick("⚠️ Delete account", "⚠️ Удалить аккаунт");
    public string DeleteAccountQuestion => Pick("Delete the account and all data on the server: transactions, categories, budgets, goals and tags?",
        "Удалить аккаунт и все данные на сервере: операции, категории, бюджеты, цели и теги?");
    public string TypeToConfirmBefore => Pick("Type", "Введите");
    public string TypeToConfirmAfter => Pick("to confirm", "для подтверждения");
    public string ConfirmWord => Pick("DELETE", "УДАЛИТЬ");

    // Errors shown in the banner: "<what failed>: <server message>"

    public string WithReason(string what, string reason) => $"{what}: {reason}";

    /// <summary>
    /// "&lt;what failed&gt;: &lt;why&gt;". API errors already arrive in the user's language;
    /// network failures and anything unexpected get a translated reason instead of the
    /// framework's English exception text.
    /// </summary>
    public string WithReason(string what, Exception error) => WithReason(what, error switch
    {
        Services.ApiException api => api.StatusCode == 429 ? TooManyRequests : api.Message,
        HttpRequestException => NoConnection,
        _ => UnexpectedError
    });

    public string NoConnection => Pick("no connection to the server", "нет связи с сервером");
    public string TooManyRequests => Pick("too many requests, wait a little", "слишком много запросов, подождите немного");
    public string UnexpectedError => Pick("unexpected error", "непредвиденная ошибка");
    public string ErrorLoadProfile => Pick("Couldn't load the profile", "Не удалось загрузить профиль");
    public string ErrorLoadData => Pick("Couldn't load data", "Не удалось загрузить данные");
    public string ErrorRefresh => Pick("Couldn't refresh", "Не удалось обновить");
    public string ErrorRefreshSummary => Pick("Couldn't refresh the summary", "Не удалось обновить сводку");
    public string ErrorChangeCurrency => Pick("Couldn't change the currency", "Не удалось сменить валюту");
    public string ErrorChangeLanguage => Pick("Couldn't change the language", "Не удалось сменить язык");
    public string ErrorSaveTransaction => Pick("Couldn't save the transaction", "Не удалось сохранить операцию");
    public string ErrorUpdateTransaction => Pick("Couldn't update the transaction", "Не удалось обновить операцию");
    public string ErrorDeleteTransaction => Pick("Couldn't delete the transaction", "Не удалось удалить операцию");
    public string ErrorExport => Pick("Couldn't export", "Не удалось экспортировать");
    public string ErrorCreateRecurring => Pick("Couldn't create the recurring transaction", "Не удалось создать повторяющуюся операцию");
    public string ErrorRunRecurring => Pick("Couldn't record it", "Не удалось выполнить");
    public string ErrorDelete => Pick("Couldn't delete", "Не удалось удалить");
    public string ErrorCreateCategory => Pick("Couldn't create the category", "Не удалось создать категорию");
    public string ErrorDeleteCategory => Pick("Couldn't delete the category", "Не удалось удалить категорию");
    public string ErrorArchive => Pick("Couldn't archive", "Не удалось архивировать");
    public string ErrorCreateBudget => Pick("Couldn't create the budget", "Не удалось создать бюджет");
    public string ErrorUpdateBudget => Pick("Couldn't change the budget", "Не удалось изменить бюджет");
    public string ErrorDeleteBudget => Pick("Couldn't delete the budget", "Не удалось удалить бюджет");
    public string ErrorCreateGoal => Pick("Couldn't create the goal", "Не удалось создать цель");
    public string ErrorUpdateGoal => Pick("Couldn't change the goal", "Не удалось изменить цель");
    public string ErrorDeleteGoal => Pick("Couldn't delete the goal", "Не удалось удалить цель");
    public string ErrorContribute => Pick("Couldn't add the contribution", "Не удалось внести вклад");
    public string ErrorCreateTag => Pick("Couldn't create the tag", "Не удалось создать тег");
    public string ErrorUpdateTag => Pick("Couldn't change the tag", "Не удалось изменить тег");
    public string ErrorDeleteTag => Pick("Couldn't delete the tag", "Не удалось удалить тег");
    public string ErrorCreateWallet => Pick("Couldn't create the wallet", "Не удалось создать кошелёк");
    public string ErrorJoin => Pick("Couldn't join", "Не удалось присоединиться");
    public string ErrorLoadWallet => Pick("Couldn't load the wallet", "Не удалось загрузить кошелёк");
    public string ErrorRegenerate => Pick("Couldn't change the code", "Не удалось сменить код");
    public string ErrorRemoveMember => Pick("Couldn't remove the member", "Не удалось исключить участника");
    public string ErrorLeave => Pick("Couldn't leave the wallet", "Не удалось выйти из кошелька");
    public string ErrorDeleteWallet => Pick("Couldn't delete the wallet", "Не удалось удалить кошелёк");
    public string ErrorJoinFromLink => Pick("Couldn't join the wallet", "Не удалось присоединиться к кошельку");
    public string ErrorDeleteAccount => Pick("Couldn't delete the account", "Не удалось удалить аккаунт");

    public string EnterCategoryName => Pick("Enter a category name.", "Введите название категории.");
    public string EnterGoalName => Pick("Enter a goal name.", "Введите название цели.");
    public string EnterTagName => Pick("Enter a tag name.", "Введите название тега.");
    public string EnterWalletName => Pick("Enter a wallet name.", "Введите название кошелька.");
    public string EnterInviteCode => Pick("Enter the invite code.", "Введите код приглашения.");
    public string EndAfterStart => Pick("The end date must be after the start date.", "Дата окончания должна быть позже даты начала.");
    public string AmountMustBePositive => Pick("The amount must be greater than zero.", "Сумма должна быть больше нуля.");
    public string AmountTooLarge => Pick("The amount is too large.", "Слишком большая сумма.");
}
