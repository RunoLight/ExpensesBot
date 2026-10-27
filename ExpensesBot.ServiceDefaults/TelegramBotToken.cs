using Microsoft.Extensions.Configuration;

namespace ExpensesBot.ServiceDefaults;

/// <summary>
/// The one bot-token lookup shared by the API and the Worker, so both read it from the same places.
/// </summary>
public static class TelegramBotToken
{
    /// <summary>
    /// Reads the bot token from "Telegram:BotToken" (appsettings, user-secrets,
    /// Telegram__BotToken env var) and falls back to the TELEGRAM_BOT_TOKEN
    /// environment variable.
    /// </summary>
    public static string? Get(IConfiguration configuration)
    {
        var token = configuration["Telegram:BotToken"];
        return string.IsNullOrWhiteSpace(token)
            ? configuration["TELEGRAM_BOT_TOKEN"]
            : token;
    }
}
