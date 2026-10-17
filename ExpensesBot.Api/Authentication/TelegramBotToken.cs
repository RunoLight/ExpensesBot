namespace ExpensesBot.Api.Authentication;

public static class TelegramBotToken
{
    /// <summary>
    /// Reads the bot token from "Telegram:BotToken" (appsettings, user-secrets,
    /// Telegram__BotToken env var) and falls back to the TELEGRAM_BOT_TOKEN
    /// environment variable. The Worker uses the same lookup.
    /// </summary>
    public static string? Get(IConfiguration configuration)
    {
        var token = configuration["Telegram:BotToken"];
        return string.IsNullOrWhiteSpace(token)
            ? configuration["TELEGRAM_BOT_TOKEN"]
            : token;
    }
}
