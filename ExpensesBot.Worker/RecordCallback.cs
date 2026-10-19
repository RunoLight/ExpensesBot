using System.Globalization;
using ExpensesBot.Data;
using ExpensesBot.Data.Entities;

namespace ExpensesBot.Worker;

/// <summary>
/// callback_data of a button that records a transaction:
/// <c>tx:{e|i}:{amount}[:{currency}[:{categoryId}]]</c>, e.g. <c>tx:e:1500:AMD:12</c>.
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

    public string Build()
    {
        var data = $"{Prefix}{TypeCode(Type)}:{Amount.ToString("0.##", CultureInfo.InvariantCulture)}";

        if (Currency is not null || CategoryId is not null)
            data += ":" + Currency;

        if (CategoryId is not null)
            data += ":" + CategoryId.Value.ToString(CultureInfo.InvariantCulture);

        return data;
    }

    public static bool TryParse(string data, out RecordCallback callback)
    {
        callback = null!;

        if (!data.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var parts = data[Prefix.Length..].Split(':');
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
