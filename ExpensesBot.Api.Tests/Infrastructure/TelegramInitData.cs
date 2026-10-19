using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ExpensesBot.Api.Tests.Infrastructure;

/// <summary>
/// Builds initData signed exactly like Telegram does, so tests can exercise the
/// real authentication path.
/// </summary>
public static class TelegramInitData
{
    public static string Create(
        string botToken,
        long telegramUserId,
        string firstName = "Test",
        DateTimeOffset? authDate = null,
        string? languageCode = null)
    {
        var user = languageCode is null
            ? JsonSerializer.Serialize(new { id = telegramUserId, first_name = firstName })
            : JsonSerializer.Serialize(new { id = telegramUserId, first_name = firstName, language_code = languageCode });
        var authDateUnix = (authDate ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString();

        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = authDateUnix,
            ["query_id"] = "AAHdF6IQAAAAAN0XohDhrOrc",
            ["user"] = user
        };

        var dataCheckString = string.Join("\n", fields.Select(x => $"{x.Key}={x.Value}"));

        var secretKey = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("WebAppData"),
            Encoding.UTF8.GetBytes(botToken));

        var hash = Convert.ToHexString(
            HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString))).ToLowerInvariant();

        return string.Join("&", fields.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}")) +
               $"&hash={hash}";
    }
}
