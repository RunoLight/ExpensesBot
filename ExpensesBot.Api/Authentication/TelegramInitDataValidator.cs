using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace ExpensesBot.Api.Authentication;

/// <summary>
/// Validates Telegram Mini App initData as described in
/// https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app
/// </summary>
public sealed class TelegramInitDataValidator
{
    private readonly TimeSpan _maxAge;

    public TelegramInitDataValidator()
        : this(TimeSpan.FromHours(24))
    {
    }

    public TelegramInitDataValidator(TimeSpan maxAge)
    {
        _maxAge = maxAge;
    }

    public bool TryValidate(
        string initData,
        string botToken,
        out long telegramUserId)
    {
        telegramUserId = 0;

        if (string.IsNullOrWhiteSpace(initData) ||
            string.IsNullOrWhiteSpace(botToken))
        {
            return false;
        }

        var parameters = QueryHelpers.ParseQuery(initData);

        if (!parameters.TryGetValue("hash", out var hashValues) ||
            hashValues.Count != 1)
        {
            return false;
        }

        var receivedHash = hashValues[0];

        if (string.IsNullOrWhiteSpace(receivedHash) ||
            receivedHash.Length != 64)
        {
            return false;
        }

        byte[] receivedHashBytes;

        try
        {
            receivedHashBytes = Convert.FromHexString(receivedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        var dataCheckString = string.Join(
            "\n",
            parameters
                .Where(x => x.Key != "hash")
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => $"{x.Key}={x.Value}"));

        // Telegram Mini Apps: secret_key = HMAC-SHA256(
        // key: "WebAppData", message: bot token).
        var secretKey = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("WebAppData"),
            Encoding.UTF8.GetBytes(botToken));

        var calculatedHash = HMACSHA256.HashData(
            secretKey,
            Encoding.UTF8.GetBytes(dataCheckString));

        if (!CryptographicOperations.FixedTimeEquals(
                receivedHashBytes, calculatedHash))
        {
            return false;
        }

        if (!parameters.TryGetValue("auth_date", out var authDateValues) ||
            authDateValues.Count != 1 ||
            !long.TryParse(
                authDateValues[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var authDate))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var maxAgeSeconds = (long)_maxAge.TotalSeconds;

        if (authDate > now + 60 || now - authDate > maxAgeSeconds)
        {
            return false;
        }

        if (!parameters.TryGetValue("user", out var userValues) ||
            userValues.Count != 1 ||
            string.IsNullOrWhiteSpace(userValues[0]))
        {
            return false;
        }

        try
        {
            using var userJson = JsonDocument.Parse(userValues[0]);

            if (!userJson.RootElement.TryGetProperty(
                    "id", out var idElement) ||
                !idElement.TryGetInt64(out telegramUserId) ||
                telegramUserId <= 0)
            {
                telegramUserId = 0;
                return false;
            }
        }
        catch (JsonException)
        {
            telegramUserId = 0;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads "first_name last_name" from initData. Call only after
    /// <see cref="TryValidate"/> succeeded, so the values are trusted.
    /// </summary>
    public static string? ReadDisplayName(string initData)
    {
        var parameters = QueryHelpers.ParseQuery(initData);

        if (!parameters.TryGetValue("user", out var userValues) ||
            userValues.Count != 1 ||
            string.IsNullOrWhiteSpace(userValues[0]))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(userValues[0]!);
            var root = document.RootElement;

            var parts = new[] { "first_name", "last_name" }
                .Select(name => root.TryGetProperty(name, out var value) ? value.GetString() : null)
                .Where(value => !string.IsNullOrWhiteSpace(value));

            var displayName = string.Join(" ", parts);
            return string.IsNullOrWhiteSpace(displayName) ? null : displayName;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
