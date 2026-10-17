namespace ExpensesBot.Data;

/// <summary>
/// Single source of truth for the currencies the application supports.
/// </summary>
public static class Currencies
{
    public const string Default = "AMD";

    public static readonly IReadOnlyList<string> Supported = ["AMD", "USD", "RUB"];

    /// <summary>
    /// Normalizes user input ("usd", " Rub ") to an upper-case supported code.
    /// </summary>
    public static bool TryNormalize(string? input, out string code)
    {
        code = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var candidate = input.Trim().ToUpperInvariant();
        if (!Supported.Contains(candidate))
            return false;

        code = candidate;
        return true;
    }

    public static string SupportedList => string.Join(", ", Supported);
}
