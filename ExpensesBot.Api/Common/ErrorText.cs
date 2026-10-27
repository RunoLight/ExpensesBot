using ExpensesBot.Data;

namespace ExpensesBot.Api.Common;

/// <summary>
/// Error messages the API returns in problem details, in English and Russian.
/// Services describe an <see cref="Error"/> with one of these; the message is picked
/// in the caller's language only when the response is written (see
/// <see cref="ResultHttpExtensions.ToProblem"/>), so services stay language-agnostic.
/// </summary>
public sealed class ErrorText
{
    public static readonly ErrorText En = new(Languages.English);
    public static readonly ErrorText Ru = new(Languages.Russian);

    public static ErrorText For(string? language) => language == Languages.Russian ? Ru : En;

    private ErrorText(string language) => IsRussian = language == Languages.Russian;

    private bool IsRussian { get; }

    private string Pick(string en, string ru) => IsRussian ? ru : en;

    // Generic

    public string NotFound => Pick("Not found.", "Не найдено.");

    public string TooManyRequests => Pick("Too many requests. Wait a little and try again.",
        "Слишком много запросов. Подождите немного и попробуйте снова.");

    // Amounts, currencies, notes, names

    public string AmountInvalid => Pick("The amount must be positive and within the allowed limit.",
        "Сумма должна быть положительной и не больше допустимой.");

    public string TargetAmountInvalid => Pick("The target amount must be positive and within the allowed limit.",
        "Целевая сумма должна быть положительной и не больше допустимой.");

    public string ContributionInvalid => Pick("The contribution must be positive and within the allowed limit.",
        "Взнос должен быть положительным и не больше допустимого.");

    public string UnsupportedCurrency => Pick($"Supported currencies: {Currencies.SupportedList}.",
        $"Поддерживаемые валюты: {Currencies.SupportedList}.");

    public string UnsupportedLanguage => Pick($"Supported languages: {string.Join(", ", Languages.Supported)}.",
        $"Поддерживаемые языки: {string.Join(", ", Languages.Supported)}.");

    public string NoteTooLong(int max) => Pick($"A note can't be longer than {max} characters.",
        $"Заметка не может быть длиннее {max} символов.");

    public string NameLength(int max) => Pick($"The name must be 1–{max} characters.",
        $"Название должно быть от 1 до {max} символов.");

    public string DateNotUtc => Pick("The date must be specified in UTC.", "Дата должна быть указана в UTC.");

    public string EndBeforeStart => Pick("The end date must be after the start date.",
        "Дата окончания должна быть позже даты начала.");

    // Transactions

    public string UnknownTransactionType => Pick("Unknown transaction type.", "Неизвестный тип операции.");

    public string CategoryNotFound => Pick("Category not found.", "Категория не найдена.");

    public string CategoryUnavailable => Pick("The category doesn't exist or is archived.",
        "Категория не существует или в архиве.");

    public string NotWalletMember => Pick("The wallet doesn't exist or you are not its member.",
        "Кошелёк не существует или вы не его участник.");

    public string IdempotencyKeyInvalid(int max) => Pick($"Idempotency-Key must be 1 to {max} visible ASCII characters.",
        $"Idempotency-Key должен состоять из 1–{max} видимых ASCII-символов.");

    public string IdempotencyKeyReused => Pick("This Idempotency-Key was already used for a different transaction.",
        "Этот Idempotency-Key уже использован для другой операции.");

    public string NoTransactions => Pick("No transactions provided.", "Нет операций для сохранения.");

    public string TooManyTransactions(int max) => Pick($"At most {max} transactions per batch.",
        $"Не больше {max} операций за раз.");

    // Recurring rules, goals, tags

    public string UnknownPattern => Pick("The schedule must be daily, weekly, monthly or yearly.",
        "Расписание должно быть ежедневным, еженедельным, ежемесячным или ежегодным.");

    public string RecurringInactive => Pick("The recurring rule is paused.", "Повторяющееся правило приостановлено.");

    public string RecurringEnded => Pick("The recurring rule has ended.", "Повторяющееся правило завершилось.");

    public string GoalInactive => Pick("The goal is closed.", "Цель закрыта.");

    public string TagExists => Pick("A tag with this name already exists.", "Тег с таким названием уже есть.");

    // Exchange

    public string NoRate(string from, string to) => Pick($"No exchange rate for {from} → {to}.",
        $"Нет курса для {from} → {to}.");

    // Wallets

    public string TooManyWallets(int max) => Pick($"You can be in at most {max} wallets.",
        $"Можно состоять не больше чем в {max} кошельках.");

    public string InviteNotFound => Pick("Invite code not found.", "Код приглашения не найден.");

    public string WalletFull(int max) => Pick($"The wallet already has {max} members.",
        $"В кошельке уже {max} участников.");

    public string OwnerCantLeave => Pick("The owner can't leave the wallet; delete it instead.",
        "Владелец не может выйти из кошелька — его можно только удалить.");

    public string OwnerCantRemoveSelf => Pick("The owner can't remove themselves; delete the wallet instead.",
        "Владелец не может удалить себя — удалите кошелёк целиком.");

    public string MemberNotFound => Pick("Member not found.", "Участник не найден.");

    public string OwnerOnly => Pick("Only the wallet owner can do this.", "Это может сделать только владелец кошелька.");
}
