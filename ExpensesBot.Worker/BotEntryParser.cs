using System.Globalization;
using System.Text.RegularExpressions;
using ExpensesBot.Data.Entities;

namespace ExpensesBot.Worker;

/// <summary>
/// What a chat message says about a transaction. <see cref="Currency"/> and
/// <see cref="Type"/> are null when the message does not say.
/// </summary>
public sealed record BotEntry(decimal Amount, string? Currency, TransactionType? Type, string? Note);

/// <summary>
/// Parses free-form chat messages such as "1500 кофе", "+50000 зарплата",
/// "20$ такси", "такси 700", "1,5к продукты" or "-300 руб метро".
/// The amount must come first or last; everything else is the note.
/// </summary>
public static partial class BotEntryParser
{
    public const decimal MaxAmount = 1_000_000_000m;
    public const int MaxNoteLength = 200;

    // Sign, currency symbol before the number, the number ("1500", "1 500", "99,90"),
    // a thousands multiplier glued to it ("2к", "1.5k", "3 тыс") and a currency after it.
    private const string AmountPattern =
        @"(?<sign>[+\-−])?\s*" +
        @"(?<pre>[$₽֏])?\s*" +
        @"(?<num>\d{1,3}(?:[  ]\d{3})+(?:[.,]\d{1,2})?|\d+(?:[.,]\d{1,2})?)" +
        @"(?<mult>(?:к|k)(?!\p{L})|\s?тыс\.?(?!\p{L}))?" +
        @"(?:\s?(?<post>[$₽֏]|(?:usd|rub|amd|долл(?:ар(?:а|ов)?)?|бакс(?:а|ов)?|руб(?:л(?:ь|я|ей))?|р|драм(?:а|ов)?)(?!\p{L}))\.?)?";

    [GeneratedRegex("^" + AmountPattern + @"(?:\s+(?<note>.+))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex AmountFirst();

    [GeneratedRegex(@"^(?<note>.+?)\s+" + AmountPattern + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex AmountLast();

    public static bool TryParse(string? text, out BotEntry entry)
    {
        entry = null!;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var input = text.Trim();
        if (input.StartsWith('/'))
            return false;

        var match = AmountFirst().Match(input);
        if (!match.Success)
            match = AmountLast().Match(input);

        if (!match.Success || !TryReadAmount(match, out var amount))
            return false;

        var currency = CurrencyFrom(match.Groups["pre"].Value) ?? CurrencyFrom(match.Groups["post"].Value);

        TransactionType? type = match.Groups["sign"].Value switch
        {
            "+" => TransactionType.Income,
            "-" or "−" => TransactionType.Expense,
            _ => null
        };

        entry = new BotEntry(amount, currency, type, CleanNote(match.Groups["note"].Value));
        return true;
    }

    /// <summary>Maps "$", "usd", "долларов", "₽", "руб", "р.", "֏", "драм" to a currency code.</summary>
    public static string? CurrencyFrom(string token)
    {
        var value = token.Trim().TrimEnd('.').ToLowerInvariant();

        return value switch
        {
            "" => null,
            "$" or "usd" => "USD",
            "₽" or "rub" or "р" => "RUB",
            "֏" or "amd" => "AMD",
            "долл" or "доллар" or "доллара" or "долларов" or "бакс" or "бакса" or "баксов" => "USD",
            "руб" or "рубль" or "рубля" or "рублей" => "RUB",
            "драм" or "драма" or "драмов" => "AMD",
            _ => null
        };
    }

    private static bool TryReadAmount(Match match, out decimal amount)
    {
        var number = match.Groups["num"].Value
            .Replace(" ", "")
            .Replace(" ", "")
            .Replace(',', '.');

        if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount))
            return false;

        if (match.Groups["mult"].Success && match.Groups["mult"].Length > 0)
            amount *= 1000m;

        return amount > 0 && amount <= MaxAmount && decimal.Round(amount, 2) == amount;
    }

    private static string? CleanNote(string note)
    {
        var cleaned = string.Join(' ', note.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (cleaned.Length == 0)
            return null;

        return cleaned.Length > MaxNoteLength ? cleaned[..MaxNoteLength] : cleaned;
    }
}
