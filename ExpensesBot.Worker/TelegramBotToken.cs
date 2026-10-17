namespace ExpensesBot.Worker;

public static class TelegramBotToken
{
    /// <summary>
    /// Same lookup as the API: "Telegram:BotToken" first, then the
    /// TELEGRAM_BOT_TOKEN environment variable.
    /// </summary>
    public static string? Get(IConfiguration configuration)
    {
        var token = configuration["Telegram:BotToken"];
        return string.IsNullOrWhiteSpace(token)
            ? configuration["TELEGRAM_BOT_TOKEN"]
            : token;
    }
}
