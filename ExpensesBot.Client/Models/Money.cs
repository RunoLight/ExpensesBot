namespace ExpensesBot.Client.Models;

public static class Money
{
    public static readonly IReadOnlyList<string> Currencies = ["AMD", "USD", "RUB"];

    public static long ToMinor(double amount) =>
        (long)Math.Round(amount * 100, 0, MidpointRounding.AwayFromZero);

    public static string Format(long amountMinor) =>
        (amountMinor / 100m).ToString("N2");

    /// <summary>"+1,500.00 AMD" for income, "-1,500.00 AMD" for expenses.</summary>
    public static string FormatSigned(long amountMinor, string currency, TransactionType type) =>
        $"{(type == TransactionType.Income ? "+" : "-")}{Format(amountMinor)} {currency}";
}
