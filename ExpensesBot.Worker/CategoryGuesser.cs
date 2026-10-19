namespace ExpensesBot.Worker;

public sealed record CategoryOption(long Id, string Name, string? Icon);

/// <summary>
/// Picks a likely category for a note written in the chat ("кофе", "такси до
/// аэропорта"). In order: the category the user chose last time for the same
/// note, a category whose name appears in the note, then common keywords for
/// the default categories.
/// </summary>
public static class CategoryGuesser
{
    // Keywords for the categories every new user gets (see UserFactory).
    private static readonly Dictionary<string, string[]> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Продукты"] = ["продукт", "магазин", "супермаркет", "овощ", "фрукт", "хлеб", "молоко"],
        ["Транспорт"] = ["такси", "метро", "автобус", "бензин", "заправк", "парковк", "проезд", "поезд"],
        ["Дом"] = ["аренд", "квартир", "коммунал", "свет", "интернет", "ремонт"],
        ["Кафе"] = ["кафе", "кофе", "ресторан", "обед", "ужин", "завтрак", "бар", "пицц", "доставк"],
        ["Зарплата"] = ["зарплат", "зп", "аванс", "преми"]
    };

    /// <param name="note">The note from the chat message.</param>
    /// <param name="categories">The user's active categories.</param>
    /// <param name="history">Earlier (note, category) pairs of the user, newest first.</param>
    public static CategoryOption? Guess(
        string? note,
        IReadOnlyList<CategoryOption> categories,
        IEnumerable<(string Note, long CategoryId)> history)
    {
        if (string.IsNullOrWhiteSpace(note) || categories.Count == 0)
            return null;

        var trimmed = note.Trim();

        // 1. Same note as before: repeat the user's own choice.
        foreach (var (previousNote, categoryId) in history)
        {
            if (string.Equals(previousNote.Trim(), trimmed, StringComparison.OrdinalIgnoreCase) &&
                categories.FirstOrDefault(c => c.Id == categoryId) is { } previous)
            {
                return previous;
            }
        }

        var words = Words(trimmed);

        // 2. The category is named in the note ("продукты на неделю").
        var named = categories.FirstOrDefault(c =>
            Words(c.Name).All(nameWord => words.Any(word => SameStem(word, nameWord))));
        if (named is not null)
            return named;

        // 3. Keywords for the default categories ("такси" → Транспорт).
        foreach (var category in categories)
        {
            if (Keywords.TryGetValue(category.Name, out var keywords) &&
                words.Any(word => keywords.Any(keyword => word.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))))
            {
                return category;
            }
        }

        return null;
    }

    /// <summary>
    /// Categories to offer as buttons: the guess first, then the ones the user
    /// picks most often for this kind of transaction, then alphabetically.
    /// </summary>
    public static List<CategoryOption> Suggest(
        IReadOnlyList<CategoryOption> categories,
        IReadOnlyDictionary<long, int> usage,
        long? guessedId,
        int max = 6) =>
        categories
            .OrderByDescending(c => c.Id == guessedId)
            .ThenByDescending(c => usage.GetValueOrDefault(c.Id))
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();

    private static string[] Words(string text) =>
        text.ToLowerInvariant()
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 2)
            .ToArray();

    private static readonly char[] Separators = [' ', ',', '.', ';', ':', '!', '?', '-', '(', ')', '"', '«', '»', '/'];

    /// <summary>"продукты" ~ "продуктов": same first letters, endings differ.</summary>
    private static bool SameStem(string word, string other)
    {
        var length = Math.Min(Math.Min(word.Length, other.Length), 5);
        return length >= 3 &&
               Math.Abs(word.Length - other.Length) <= 3 &&
               string.CompareOrdinal(word, 0, other, 0, length) == 0;
    }
}
