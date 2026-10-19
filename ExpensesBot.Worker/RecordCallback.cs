using System.Globalization;
using ExpensesBot.Data;
using ExpensesBot.Data.Entities;

namespace ExpensesBot.Worker;

/// <summary>
/// callback_data of the bot's buttons for a new transaction:
/// <list type="bullet">
/// <item><c>tx:{e|i}:{amount}[:{currency}[:{categoryId}]]</c> records it, e.g. <c>tx:e:1500:AMD:12</c>
/// (no category id, or 0, means "without category");</item>
/// <item><c>ty:{e|i}:{amount}:{currency}[:{guessedCategoryId}]</c> means the type was chosen
/// and the category picker should be shown next.</item>
/// </list>
/// Telegram limits callback_data to 64 bytes, so the note is not included:
/// it is read back from the user's message the question replies to.
/// </summary>
public sealed record RecordCallback(
    TransactionType Type,
    decimal Amount,
    string? Currency = null,
    long? CategoryId = null)
{
    public const string Prefix = "tx:";
    public const string TypeStepPrefix = "ty:";

    /// <summary>Button that records the transaction.</summary>
    public string Build() => Build(Prefix);

    /// <summary>Button that picks the type and moves on to choosing a category.</summary>
    public string BuildTypeStep() => Build(TypeStepPrefix);

    public static bool TryParse(string data, out RecordCallback callback) =>
        TryParse(data, Prefix, out callback);

    public static bool TryParseTypeStep(string data, out RecordCallback callback) =>
        TryParse(data, TypeStepPrefix, out callback);

    private string Build(string prefix)
    {
        var data = $"{prefix}{TypeCode(Type)}:{Amount.ToString("0.##", CultureInfo.InvariantCulture)}";

        if (Currency is not null || CategoryId is not null)
            data += ":" + Currency;

        if (CategoryId is not null)
            data += ":" + CategoryId.Value.ToString(CultureInfo.InvariantCulture);

        return data;
    }

    private static bool TryParse(string data, string prefix, out RecordCallback callback)
    {
        callback = null!;

        if (!data.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var parts = data[prefix.Length..].Split(':');
        if (parts.Length is < 2 or > 4 ||
            !TryParseType(parts[0], out var type) ||
            !TelegramBotHandler.TryParseAmount(parts[1], out var amount))
        {
            return false;
        }

        string? currency = null;
        if (parts.Length >= 3 && parts[2].Length > 0)
        {
            if (!Currencies.TryNormalize(parts[2], out var code))
                return false;
            currency = code;
        }

        long? categoryId = null;
        if (parts.Length == 4)
        {
            if (!long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                return false;
            categoryId = id > 0 ? id : null;
        }

        callback = new RecordCallback(type, amount, currency, categoryId);
        return true;
    }

    public static string TypeCode(TransactionType type) => type == TransactionType.Income ? "i" : "e";

    public static bool TryParseType(string code, out TransactionType type)
    {
        type = code == "i" ? TransactionType.Income : TransactionType.Expense;
        return code is "e" or "i";
    }
}
