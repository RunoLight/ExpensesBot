namespace ExpensesBot.Client.Models;

public sealed record CategoryDto(
    long Id,
    string Name,
    string? Icon,
    bool IsArchived);

public static class CategoryListExtensions
{
    public const string DefaultIcon = "📦";

    public static string IconFor(this IEnumerable<CategoryDto> categories, long? categoryId) =>
        categories.FirstOrDefault(c => c.Id == categoryId)?.Icon ?? DefaultIcon;

    /// <summary>Categories that can be picked in a form: active ones plus the currently selected one.</summary>
    public static IEnumerable<CategoryDto> Selectable(this IEnumerable<CategoryDto> categories, long? selectedId = null) =>
        categories.Where(c => !c.IsArchived || c.Id == selectedId);
}
