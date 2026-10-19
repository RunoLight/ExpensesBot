namespace ExpensesBot.Data;

/// <summary>Languages the bot and the Mini App speak. English is the default.</summary>
public static class Languages
{
    public const string English = "en";
    public const string Russian = "ru";
    public const string Default = English;

    public static readonly IReadOnlyList<string> Supported = [English, Russian];

    /// <summary>
    /// Maps Telegram's language_code ("ru", "en-US", "de", …) to a supported
    /// language: Russian for "ru*", English for everything else.
    /// </summary>
    public static string FromTelegram(string? languageCode) =>
        languageCode?.StartsWith(Russian, StringComparison.OrdinalIgnoreCase) == true ? Russian : English;

    /// <summary>Normalizes user input (" RU ") to a supported code.</summary>
    public static bool TryNormalize(string? input, out string language)
    {
        language = input?.Trim().ToLowerInvariant() ?? "";
        return Supported.Contains(language);
    }
}
