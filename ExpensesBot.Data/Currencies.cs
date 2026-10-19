using System.Globalization;

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

    // "12 345,67" without depending on ICU culture data being installed.
    private static readonly NumberFormatInfo MoneyFormat = new()
    {
        NumberGroupSeparator = " ",
        NumberDecimalSeparator = ","
    };

    /// <summary>Formats minor units for chat messages: "1 500,00 AMD".</summary>
    public static string FormatMoney(long amountMinor, string currency) =>
        $"{(amountMinor / 100m).ToString("N2", MoneyFormat)} {currency}";
}
