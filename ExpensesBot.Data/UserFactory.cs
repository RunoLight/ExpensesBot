using ExpensesBot.Data.Entities;

namespace ExpensesBot.Data;

/// <summary>
/// Creates new users with the same defaults no matter where they sign up
/// (Mini App, bot chat or development header).
/// </summary>
public static class UserFactory
{
    public static User Create(long telegramUserId, string? displayName)
    {
        return new User
        {
            TelegramUserId = telegramUserId,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            DefaultCurrency = Currencies.Default,
            CreatedAtUtc = DateTime.UtcNow,
            Categories =
            [
                new Category { Name = "Продукты", Icon = "🛒" },
                new Category { Name = "Транспорт", Icon = "🚕" },
                new Category { Name = "Дом", Icon = "🏠" },
                new Category { Name = "Кафе", Icon = "☕" },
                new Category { Name = "Зарплата", Icon = "💼" },
                new Category { Name = "Другое", Icon = "📦" }
            ]
        };
    }
}
