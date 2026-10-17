namespace ExpensesBot.Client.Models;

public sealed record TagDto(
    long Id,
    string Name,
    string Color,
    DateTime CreatedAtUtc);

public static class TagColors
{
    public const string Default = "#36A2EB";

    private static readonly string[] Palette =
        [Default, "#FF6384", "#4BC0C0", "#FF9F40", "#9966FF", "#FFCD56", "#C9CBCF", "#7BC67E"];

    /// <summary>The colour after <paramref name="current"/> in the palette, wrapping around.</summary>
    public static string Next(string current) =>
        Palette[(Array.IndexOf(Palette, current) + 1) % Palette.Length];
}
