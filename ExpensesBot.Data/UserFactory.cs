using ExpensesBot.Data.Entities;

namespace ExpensesBot.Data;

/// <summary>
/// Creates new users with the same defaults no matter where they sign up
/// (Mini App, bot chat or development header).
/// </summary>
public static class UserFactory
{
    /// <param name="language">"en" or "ru"; anything else becomes English. Also names the default categories.</param>
    public static User Create(long telegramUserId, string? displayName, string? language = null)
    {
        var supported = Languages.TryNormalize(language, out var normalized) ? normalized : Languages.Default;

        return new User
        {
            TelegramUserId = telegramUserId,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            DefaultCurrency = Currencies.Default,
            Language = supported,
            CreatedAtUtc = DateTime.UtcNow,
            Categories = MessageText.For(supported).DefaultCategories
                .Select(c => new Category { Name = c.Name, Icon = c.Icon })
                .ToList()
        };
    }
}
